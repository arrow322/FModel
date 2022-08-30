﻿using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.SkeletalMesh;
using CUE4Parse.UE4.Assets.Exports.StaticMesh;
using CUE4Parse.UE4.Objects.Core.Math;
using CUE4Parse_Conversion.Meshes;
using FModel.Extensions;
using ImGuiNET;
using Silk.NET.Core;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.OpenGL.Extensions.ImGui;
using Silk.NET.Windowing;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Advanced;
using SixLabors.ImageSharp.PixelFormats;

namespace FModel.Views.Snooper;

public class Snooper
{
    private IWindow _window;
    private ImGuiController _controller;
    private GL _gl;
    private Camera _camera;
    private IKeyboard _keyboard;
    private IMouse _mouse;
    private Vector2 _previousMousePosition;
    private RawImage _icon;

    private readonly Skybox _skybox;
    private readonly Grid _grid;
    private readonly List<Model> _models;

    private Shader _shader;

    private readonly int _width;
    private readonly int _height;

    public Snooper()
    {
        const double ratio = .7;
        var x = SystemParameters.MaximizedPrimaryScreenWidth;
        var y = SystemParameters.MaximizedPrimaryScreenHeight;
        _width = Convert.ToInt32(x * ratio);
        _height = Convert.ToInt32(y * ratio);

        var options = WindowOptions.Default;
        options.Size = new Vector2D<int>(_width, _height);
        options.WindowBorder = WindowBorder.Hidden;
        options.Title = "Snooper";
        options.Samples = 4;
        _window = Silk.NET.Windowing.Window.Create(options);

        unsafe
        {
            var info = Application.GetResourceStream(new Uri("/FModel;component/Resources/materialicon.png", UriKind.Relative));
            using var image = Image.Load<Rgba32>(info.Stream);
            var memoryGroup = image.GetPixelMemoryGroup();
            Memory<byte> array = new byte[memoryGroup.TotalLength * sizeof(Rgba32)];
            var block = MemoryMarshal.Cast<byte, Rgba32>(array.Span);
            foreach (var memory in memoryGroup)
            {
                memory.Span.CopyTo(block);
                block = block.Slice(memory.Length);
            }
            _icon = new RawImage(image.Width, image.Height, array);
        }

        _window.Load += OnLoad;
        _window.Update += OnUpdate;
        _window.Render += OnRender;
        _window.Closing += OnClose;
        _window.FramebufferResize += OnFramebufferResize;

        _skybox = new Skybox();
        _grid = new Grid();
        _models = new List<Model>();
    }

    public void Run(UObject export)
    {
        switch (export)
        {
            case UStaticMesh st when st.TryConvert(out var mesh):
            {
                _models.Add(new Model(st.Name, mesh.LODs[0], mesh.LODs[0].Verts));
                SetupCamera(mesh.BoundingBox *= Constants.SCALE_DOWN_RATIO);
                break;
            }
            case USkeletalMesh sk when sk.TryConvert(out var mesh):
            {
                _models.Add(new Model(sk.Name, mesh.LODs[0], mesh.LODs[0].Verts));
                SetupCamera(mesh.BoundingBox *= Constants.SCALE_DOWN_RATIO);
                break;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(export));
        }

        _window.Run();
    }

    private void SetupCamera(FBox box)
    {
        var far = box.Max.Max();
        var center = box.GetCenter();
        var position = new Vector3(0f, center.Z, box.Max.Y * 3);
        _camera = new Camera(position, center, 0.01f, far * 50f, far / 2f);
    }

    private void OnLoad()
    {
        _window.SetWindowIcon(ref _icon);
        _window.Center();

        var input = _window.CreateInput();
        _keyboard = input.Keyboards[0];
        _keyboard.KeyDown += KeyDown;
        _mouse = input.Mice[0];
        _mouse.MouseDown += OnMouseDown;
        _mouse.MouseUp += OnMouseUp;
        _mouse.MouseMove += OnMouseMove;
        _mouse.Scroll += OnMouseWheel;

        _gl = GL.GetApi(_window);
        _gl.Enable(EnableCap.Blend);
        _gl.Enable(EnableCap.DepthTest);
        _gl.Enable(EnableCap.Multisample);
        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

        _controller = new ImGuiController(_gl, _window, input);

        // ImGuiExtensions.Theme();

        _skybox.Setup(_gl);
        _grid.Setup(_gl);

        _shader = new Shader(_gl);
        foreach (var model in _models)
        {
            model.Setup(_gl);
        }
    }

    private void OnFramebufferResize(Vector2D<int> size)
    {
        _gl.Viewport(size);
    }

    private void OnRender(double deltaTime)
    {
        _controller.Update((float) deltaTime);

        _gl.ClearColor(0.102f, 0.102f, 0.129f, 1.0f);
        _gl.Clear((uint) ClearBufferMask.ColorBufferBit | (uint) ClearBufferMask.DepthBufferBit);

        _skybox.Bind(_camera);
        _grid.Bind(_camera);

        _shader.Use();

        _shader.SetUniform("uModel", Matrix4x4.Identity);
        _shader.SetUniform("uView", _camera.GetViewMatrix());
        _shader.SetUniform("uProjection", _camera.GetProjectionMatrix());
        _shader.SetUniform("viewPos", _camera.Position);

        _shader.SetUniform("material.diffuseMap", 0);
        _shader.SetUniform("material.normalMap", 1);
        _shader.SetUniform("material.specularMap", 2);
        _shader.SetUniform("material.emissionMap", 3);

        ImGuiExtensions.DrawNavbar();
        ImGui.Begin("ImGui.NET", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoSavedSettings);
        foreach (var model in _models)
        {
            model.Bind(_shader);
        }
        ImGui.End();
        ImGuiExtensions.DrawFPS();

        _shader.SetUniform("light.position", _camera.Position);

        _controller.Render();
    }

    private void OnUpdate(double deltaTime)
    {
        var multiplier = _keyboard.IsKeyPressed(Key.ShiftLeft) ? 2f : 1f;
        var moveSpeed = _camera.Speed * multiplier * (float) deltaTime;
        if (_keyboard.IsKeyPressed(Key.W))
        {
            _camera.Position += moveSpeed * _camera.Direction;
        }
        if (_keyboard.IsKeyPressed(Key.S))
        {
            _camera.Position -= moveSpeed * _camera.Direction;
        }
        if (_keyboard.IsKeyPressed(Key.A))
        {
            _camera.Position -= Vector3.Normalize(Vector3.Cross(_camera.Direction, _camera.Up)) * moveSpeed;
        }
        if (_keyboard.IsKeyPressed(Key.D))
        {
            _camera.Position += Vector3.Normalize(Vector3.Cross(_camera.Direction, _camera.Up)) * moveSpeed;
        }
        if (_keyboard.IsKeyPressed(Key.E))
        {
            _camera.Position += moveSpeed * _camera.Up;
        }
        if (_keyboard.IsKeyPressed(Key.Q))
        {
            _camera.Position -= moveSpeed * _camera.Up;
        }
    }

    private void OnMouseDown(IMouse mouse, MouseButton button)
    {
        if (button != MouseButton.Left) return;
        mouse.Cursor.CursorMode = CursorMode.Raw;
    }

    private void OnMouseUp(IMouse mouse, MouseButton button)
    {
        if (button != MouseButton.Left) return;
        mouse.Cursor.CursorMode = CursorMode.Normal;
    }

    private void OnMouseMove(IMouse mouse, Vector2 position)
    {
        if (_previousMousePosition == default) { _previousMousePosition = position; }
        else
        {
            if (mouse.Cursor.CursorMode == CursorMode.Raw)
            {
                const float lookSensitivity = 0.1f;
                var xOffset = (position.X - _previousMousePosition.X) * lookSensitivity;
                var yOffset = (position.Y - _previousMousePosition.Y) * lookSensitivity;

                _camera.ModifyDirection(xOffset, yOffset);
            }

            _previousMousePosition = position;
        }
    }

    private void OnMouseWheel(IMouse mouse, ScrollWheel scrollWheel)
    {
        _camera.ModifyZoom(scrollWheel.Y);
    }

    private void OnClose()
    {
        _grid.Dispose();
        _skybox.Dispose();
        _shader.Dispose();
        foreach (var model in _models)
        {
            model.Dispose();
        }
        _models.Clear();
        _controller.Dispose();
        _window.Dispose();
        _gl.Dispose();
    }

    private void KeyDown(IKeyboard keyboard, Key key, int arg3)
    {
        switch (key)
        {
            case Key.Escape:
                _window.Close();
                break;
        }
    }
}
