﻿using System;
using System.Collections.Generic;
using System.Numerics;
using System.Windows;
using CUE4Parse.UE4.Objects.Core.Misc;
using FModel.Creator;
using ImGuiNET;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.OpenGL.Extensions.ImGui;
using Silk.NET.Windowing;

namespace FModel.Views.Snooper;

public class SnimGui : IDisposable
{
    private readonly ImGuiController _controller;
    private readonly GraphicsAPI _api;
    private readonly string _renderer;

    private readonly Vector2 _outlinerSize;
    private readonly Vector2 _outlinerPosition;
    private readonly Vector2 _propertiesSize;
    private readonly Vector2 _propertiesPosition;
    private readonly Vector2 _viewportSize;
    private readonly Vector2 _viewportPosition;
    private readonly Vector2 _textureSize;
    private readonly Vector2 _texturePosition;
    private bool _viewportFocus;
    private FGuid _selectedModel;
    private int _selectedInstance;
    private int _selectedSection;

    private const ImGuiWindowFlags _noResize = ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove; // delete once we have a proper docking branch
    private const ImGuiCond _firstUse = ImGuiCond.Appearing; // switch to FirstUseEver once the docking branch will not be useful anymore...
    private const uint _dockspaceId = 1337;

    public SnimGui(GL gl, IWindow window, IInputContext input)
    {
        var fontConfig = new ImGuiFontConfig("C:\\Windows\\Fonts\\segoeui.ttf", 16);
        _controller = new ImGuiController(gl, window, input, fontConfig);
        _api = window.API;
        _renderer = gl.GetStringS(StringName.Renderer);

        var style = ImGui.GetStyle();
        var viewport = ImGui.GetMainViewport();
        var titleBarHeight = ImGui.GetFontSize() + style.FramePadding.Y * 2;

        _outlinerSize = new Vector2(400, 300);
        _outlinerPosition = new Vector2(viewport.WorkSize.X - _outlinerSize.X, titleBarHeight);
        _propertiesSize = _outlinerSize with { Y = viewport.WorkSize.Y - _outlinerSize.Y - titleBarHeight };
        _propertiesPosition = new Vector2(viewport.WorkSize.X - _propertiesSize.X, _outlinerPosition.Y + _outlinerSize.Y);
        _viewportSize = _outlinerPosition with { Y = viewport.WorkSize.Y - titleBarHeight - 150 };
        _viewportPosition = new Vector2(0, titleBarHeight);
        _textureSize = _viewportSize with { Y = viewport.WorkSize.Y - _viewportSize.Y - titleBarHeight };
        _texturePosition = new Vector2(0, _viewportPosition.Y + _viewportSize.Y);
        _selectedModel = new FGuid();
        _selectedInstance = 0;
        _selectedSection = 0;

        Theme(style);
    }

    public void Increment(FGuid guid) => _selectedModel = guid;

    public void Construct(Vector2D<int> size, FramebufferObject framebuffer, Camera camera, IMouse mouse, IDictionary<FGuid, Model> models)
    {
        DrawDockSpace(size);
        DrawNavbar();

        DrawOuliner(camera, models);
        DrawProperties(camera, models);
        DrawTextures(models);
        Draw3DViewport(framebuffer, camera, mouse);
    }

    /// <summary>
    /// absolutely useless at the moment since ImGui.NET lacks DockerBuilder bindinds
    /// </summary>
    private void DrawDockSpace(Vector2D<int> size)
    {
        const ImGuiWindowFlags flags =
            ImGuiWindowFlags.MenuBar | ImGuiWindowFlags.NoDocking |
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize |
            ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoNavFocus;

        ImGui.SetNextWindowPos(new Vector2(0, 0));
        ImGui.SetNextWindowSize(new Vector2(size.X, size.Y));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.Begin("Snooper", flags);
        ImGui.PopStyleVar();
        ImGui.DockSpace(_dockspaceId);
    }

    private void DrawNavbar()
    {
        if (!ImGui.BeginMainMenuBar()) return;

        if (ImGui.BeginMenu("Window"))
        {
            ImGui.MenuItem("Append", "H");
            ImGui.MenuItem("Close", "ESC");
            ImGui.EndMenu();
        }
        if (ImGui.BeginMenu("Edit"))
        {
            if (ImGui.MenuItem("Undo", "CTRL+Z")) {}
            if (ImGui.MenuItem("Redo", "CTRL+Y", false, false)) {}  // Disabled item
            ImGui.Separator();
            if (ImGui.MenuItem("Cut", "CTRL+X")) {}
            if (ImGui.MenuItem("Copy", "CTRL+C")) {}
            if (ImGui.MenuItem("Paste", "CTRL+V")) {}
            ImGui.EndMenu();
        }

        const string text = "Press ESC to Exit...";
        ImGui.SetCursorPosX(ImGui.GetWindowViewport().WorkSize.X - ImGui.CalcTextSize(text).X - 5);
        ImGui.TextColored(new Vector4(0.36f, 0.42f, 0.47f, 1.00f), text); // ImGuiCol.TextDisabled

        ImGui.EndMainMenuBar();
    }

    private void DrawOuliner(Camera camera, IDictionary<FGuid, Model> models)
    {
        ImGui.SetNextWindowSize(_outlinerSize, _firstUse);
        ImGui.SetNextWindowPos(_outlinerPosition, _firstUse);
        ImGui.Begin("Scene", _noResize | ImGuiWindowFlags.NoCollapse);

        ImGui.SetNextItemOpen(true, ImGuiCond.Appearing);
        if (ImGui.TreeNode("Collection"))
        {
            var i = 0;
            foreach (var (guid, model) in models)
            {
                ImGui.PushID(i);
                if (ImGui.Selectable(model.Name, _selectedModel == guid))
                {
                    _selectedModel = guid;
                    _selectedInstance = 0;
                    _selectedSection = 0;
                }
                if (ImGui.BeginPopupContextItem())
                {
                    if (ImGui.Selectable("Delete"))
                        models.Remove(guid);
                    if (ImGui.Selectable("Copy to Clipboard"))
                        Application.Current.Dispatcher.Invoke(delegate
                        {
                            Clipboard.SetText(model.Name);
                        });
                    ImGui.EndPopup();
                }
                ImGui.PopID();
                i++;
            }
            ImGui.TreePop();
        }

        if (ImGui.TreeNode("Camera"))
        {
            ImGui.Text($"Position: {camera.Position}");
            ImGui.Text($"Direction: {camera.Direction}");
            ImGui.Text($"Speed: {camera.Speed}");
            ImGui.Text($"Far: {camera.Far}");
            ImGui.Text($"Near: {camera.Near}");
            ImGui.Text($"Zoom: {camera.Zoom}");
            ImGui.TreePop();
        }

        ImGui.End();
    }

    private void DrawProperties(Camera camera, IDictionary<FGuid, Model> models)
    {
        ImGui.SetNextWindowSize(_propertiesSize, _firstUse);
        ImGui.SetNextWindowPos(_propertiesPosition, _firstUse);
        ImGui.Begin("Properties", _noResize | ImGuiWindowFlags.NoCollapse);

        if (!models.TryGetValue(_selectedModel, out var model)) return;
        ImGui.Text($"Type: {model.Type}");
        ImGui.Text($"Entity: {model.Name}");
        ImGui.Separator();
        if (ImGui.Button("Focus"))
            camera.Position = model.Transforms[_selectedInstance].Position;
        ImGui.SameLine();
        ImGui.BeginDisabled(model.TransformsCount < 2);
        ImGui.SliderInt("Instance", ref _selectedInstance, 0, model.TransformsCount - 1, "%i", ImGuiSliderFlags.AlwaysClamp);
        ImGui.EndDisabled();
        ImGui.BeginDisabled(!model.HasVertexColors);
        ImGui.Checkbox("Vertex Colors", ref model.DisplayVertexColors);
        ImGui.EndDisabled();
        ImGui.BeginDisabled(!model.HasBones);
        ImGui.Checkbox("Bones", ref model.DisplayBones);
        ImGui.EndDisabled();

        if (ImGui.TreeNode("Transform"))
        {
            const int width = 100;
            var speed = camera.Speed / 100;
            var index = 0;

            ImGui.SetNextItemWidth(width); ImGui.PushID(index);
            ImGui.DragFloat(model.TransformsLabels[index], ref model.Transforms[_selectedInstance].Position.X, speed, 0f, 0f, "%.2f m");
            ImGui.PopID();

            index++; ImGui.SetNextItemWidth(width); ImGui.PushID(index);
            ImGui.DragFloat(model.TransformsLabels[index], ref model.Transforms[_selectedInstance].Position.Y, speed, 0f, 0f, "%.2f m");
            ImGui.PopID();

            index++; ImGui.SetNextItemWidth(width); ImGui.PushID(index);
            ImGui.DragFloat(model.TransformsLabels[index], ref model.Transforms[_selectedInstance].Position.Z, speed, 0f, 0f, "%.2f m");
            ImGui.PopID();

            ImGui.Spacing();

            index++; ImGui.SetNextItemWidth(width); ImGui.PushID(index);
            ImGui.DragFloat(model.TransformsLabels[index], ref model.Transforms[_selectedInstance].Rotation.Pitch, .5f, 0f, 0f, "%.1f°");
            ImGui.PopID();

            index++; ImGui.SetNextItemWidth(width); ImGui.PushID(index);
            ImGui.DragFloat(model.TransformsLabels[index], ref model.Transforms[_selectedInstance].Rotation.Roll, .5f, 0f, 0f, "%.1f°");
            ImGui.PopID();

            index++; ImGui.SetNextItemWidth(width); ImGui.PushID(index);
            ImGui.DragFloat(model.TransformsLabels[index], ref model.Transforms[_selectedInstance].Rotation.Yaw, .5f, 0f, 0f, "%.1f°");
            ImGui.PopID();

            ImGui.Spacing();

            index++; ImGui.SetNextItemWidth(width); ImGui.PushID(index);
            ImGui.DragFloat(model.TransformsLabels[index], ref model.Transforms[_selectedInstance].Scale.X, speed, 0f, 0f, "%.3f");
            ImGui.PopID();

            index++; ImGui.SetNextItemWidth(width); ImGui.PushID(index);
            ImGui.DragFloat(model.TransformsLabels[index], ref model.Transforms[_selectedInstance].Scale.Y, speed, 0f, 0f, "%.3f");
            ImGui.PopID();

            index++; ImGui.SetNextItemWidth(width); ImGui.PushID(index);
            ImGui.DragFloat(model.TransformsLabels[index], ref model.Transforms[_selectedInstance].Scale.Z, speed, 0f, 0f, "%.3f");
            ImGui.PopID();

            model.UpdateMatrix(_selectedInstance);
            ImGui.TreePop();
        }

        ImGui.SetNextItemOpen(true, ImGuiCond.Appearing);
        if (ImGui.TreeNode("Materials"))
        {
            ImGui.BeginTable("Sections", 2, ImGuiTableFlags.Borders | ImGuiTableFlags.Resizable);
            ImGui.TableSetupColumn("Index", ImGuiTableColumnFlags.NoHeaderWidth | ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableSetupColumn("Name");
            ImGui.TableHeadersRow();
            for (var i = 0; i < model.Sections.Length; i++)
            {
                var section = model.Sections[i];

                ImGui.PushID(i);
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                if (!section.Show)
                    ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(new Vector4(1, 0, 0, .5f)));
                ImGui.Text(section.Index.ToString("D"));
                ImGui.TableNextColumn();
                if (ImGui.Selectable(section.Name, _selectedSection == i, ImGuiSelectableFlags.SpanAllColumns))
                    _selectedSection = i;
                ImGui.PopID();
            }
            ImGui.EndTable();
            ImGui.TreePop();
        }

        ImGui.End();
    }

    private void DrawTextures(IDictionary<FGuid, Model> models)
    {
        ImGui.SetNextWindowSize(_textureSize, _firstUse);
        ImGui.SetNextWindowPos(_texturePosition, _firstUse);
        ImGui.Begin("Textures", _noResize | ImGuiWindowFlags.NoCollapse);

        if (!models.TryGetValue(_selectedModel, out var model)) return;
        var section = model.Sections[_selectedSection];
        ImGui.BeginGroup();
        ImGui.Checkbox("Show", ref section.Show);
        ImGui.Checkbox("Wireframe", ref section.Wireframe);
        ImGui.EndGroup();
        ImGui.SameLine();
        ImGui.BeginGroup();
        if (section.HasDiffuseColor)
        {
            ImGui.SetNextItemWidth(300);
            ImGui.ColorEdit4(section.TexturesLabels[0], ref section.DiffuseColor, ImGuiColorEditFlags.AlphaPreview | ImGuiColorEditFlags.AlphaBar);
        }
        else
        {
            for (var i = 0; i < section.Textures.Length; i++)
            {
                if (section.Textures[i] is not {} texture)
                    continue;

                ImGui.SameLine();
                ImGui.BeginGroup();
                ImGui.Image(texture.GetPointer(), new Vector2(88), Vector2.Zero, Vector2.One, Vector4.One, new Vector4(1, 1, 1, .5f));
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.Text($"Type: ({texture.Format}) {texture.Type}:{texture.Name}");
                    ImGui.Text($"Texture: {texture.Path}");
                    ImGui.Text($"Imported: {texture.ImportedWidth}x{texture.ImportedHeight}");
                    ImGui.Text($"Mip Used: {texture.Width}x{texture.Height}");
                    ImGui.Spacing();
                    ImGui.TextDisabled(texture.Label);
                    ImGui.EndTooltip();
                }

                if (ImGui.IsItemClicked())
                {
                    Application.Current.Dispatcher.Invoke(delegate
                    {
                        Clipboard.SetText(Utils.FixPath(texture.Path));
                        texture.Label = "(?) Copied to Clipboard";
                    });
                }

                if (i == 3) // emissive, show color
                {
                    ImGui.SameLine();
                    ImGui.SetNextItemWidth(300);
                    ImGui.ColorEdit4($"{section.TexturesLabels[i]} Color", ref section.EmissionColor, ImGuiColorEditFlags.NoAlpha);
                }
                var text = section.TexturesLabels[i];
                var width = ImGui.GetCursorPos().X;
                ImGui.SetCursorPosX(width + ImGui.CalcTextSize(text).X * 0.5f);
                ImGui.Text(text);
                ImGui.EndGroup();
            }
        }
        ImGui.EndGroup();

        // ImGui.Text($"Faces: {FacesCount} ({Math.Round(FacesCount / indices * 100f, 2)}%%)");
        // ImGui.Text($"First Face: {FirstFaceIndex}");
        // ImGui.Separator();
        // if (_hasDiffuseColor)
        // {
        //     ImGui.ColorEdit4("Diffuse Color", ref _diffuseColor, ImGuiColorEditFlags.NoInputs);
        // }
        // else
        // {
        //     ImGui.Text($"Diffuse: ({Parameters.Diffuse?.ExportType}) {Parameters.Diffuse?.Name}");
        //     ImGui.Text($"Normal: ({Parameters.Normal?.ExportType}) {Parameters.Normal?.Name}");
        //     ImGui.Text($"Specular: ({Parameters.Specular?.ExportType}) {Parameters.Specular?.Name}");
        //     if (Parameters.HasTopEmissiveTexture)
        //         ImGui.Text($"Emissive: ({Parameters.Emissive?.ExportType}) {Parameters.Emissive?.Name}");
        //     ImGui.Separator();
        // }

        ImGui.End();
    }

    private void Draw3DViewport(FramebufferObject framebuffer, Camera camera, IMouse mouse)
    {
        const float lookSensitivity = 0.1f;
        const ImGuiWindowFlags flags =
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse |
            ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.AlwaysUseWindowPadding;

        ImGui.SetNextWindowSize(_viewportSize, _firstUse);
        ImGui.SetNextWindowPos(_viewportPosition, _firstUse);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.Begin($"Viewport ({_api.API} {_api.Version.MajorVersion}.{_api.Version.MinorVersion}) ({_renderer})", _noResize | flags);
        ImGui.PopStyleVar();

        var largest = ImGui.GetContentRegionAvail();
        largest.X -= ImGui.GetScrollX();
        largest.Y -= ImGui.GetScrollY();

        var width = largest.X;
        var height = width / camera.AspectRatio;
        if (height > largest.Y)
        {
            height = largest.Y;
            width = height * camera.AspectRatio;
        }

        var pos = new Vector2(largest.X / 2f - width / 2f + ImGui.GetCursorPosX(), largest.Y / 2f - height / 2f + ImGui.GetCursorPosY());
        var size = new Vector2(width, height);
        ImGui.SetCursorPos(pos);
        ImGui.ImageButton(framebuffer.GetPointer(), size, new Vector2(0, 1), new Vector2(1, 0), 0);

        // it took me 5 hours to make it work, don't change any of the following code
        // basically the Raw cursor doesn't actually freeze the mouse position
        // so for ImGui, the IsItemHovered will be false if mouse leave, even in Raw mode
        var io = ImGui.GetIO();
        if (ImGui.IsItemHovered())
        {
            camera.ModifyZoom(io.MouseWheel);

            // if left button down while mouse is hover viewport
            if (ImGui.IsMouseDown(ImGuiMouseButton.Left) && !_viewportFocus)
                _viewportFocus = true;
        }

        // this can't be inside IsItemHovered! read it as
        // if left mouse button was pressed while hovering the viewport
        // move camera until left mouse button is released
        // no matter where mouse position end up
        if (ImGui.IsMouseDragging(ImGuiMouseButton.Left, lookSensitivity) && _viewportFocus)
        {
            var delta = io.MouseDelta * lookSensitivity;
            camera.ModifyDirection(delta.X, delta.Y);
            mouse.Cursor.CursorMode = CursorMode.Raw;
        }

        // if left button up and mouse was in viewport
        if (ImGui.IsMouseReleased(ImGuiMouseButton.Left) && _viewportFocus)
        {
            _viewportFocus = false;
            mouse.Cursor.CursorMode = CursorMode.Normal;
        }

        const float padding = 5f;
        float framerate = ImGui.GetIO().Framerate;
        var text = $"FPS: {framerate:0} ({1000.0f / framerate:0.##} ms)";
        ImGui.SetCursorPos(new Vector2(pos.X + padding, pos.Y + size.Y - ImGui.CalcTextSize(text).Y - padding));
        ImGui.Text(text);

        ImGui.End();
    }

    private void Theme(ImGuiStylePtr style)
    {
        var io = ImGui.GetIO();
        io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;
        io.ConfigFlags |= ImGuiConfigFlags.ViewportsEnable;
        io.ConfigWindowsMoveFromTitleBarOnly = true;
        io.ConfigDockingWithShift = true;

        style.WindowMenuButtonPosition = ImGuiDir.Right;
        style.ScrollbarSize = 10f;
        // style.Colors[(int) ImGuiCol.Text] = new Vector4(0.95f, 0.96f, 0.98f, 1.00f);
        // style.Colors[(int) ImGuiCol.TextDisabled] = new Vector4(0.36f, 0.42f, 0.47f, 1.00f);
        // style.Colors[(int) ImGuiCol.WindowBg] = new Vector4(0.149f, 0.149f, 0.188f, 0.35f);
        // style.Colors[(int) ImGuiCol.ChildBg] = new Vector4(0.15f, 0.18f, 0.22f, 1.00f);
        // style.Colors[(int) ImGuiCol.PopupBg] = new Vector4(0.08f, 0.08f, 0.08f, 0.94f);
        // style.Colors[(int) ImGuiCol.Border] = new Vector4(0.08f, 0.10f, 0.12f, 1.00f);
        // style.Colors[(int) ImGuiCol.BorderShadow] = new Vector4(0.00f, 0.00f, 0.00f, 0.00f);
        // style.Colors[(int) ImGuiCol.FrameBg] = new Vector4(0.20f, 0.25f, 0.29f, 1.00f);
        // style.Colors[(int) ImGuiCol.FrameBgHovered] = new Vector4(0.12f, 0.20f, 0.28f, 1.00f);
        // style.Colors[(int) ImGuiCol.FrameBgActive] = new Vector4(0.09f, 0.12f, 0.14f, 1.00f);
        // style.Colors[(int) ImGuiCol.TitleBg] = new Vector4(0.09f, 0.12f, 0.14f, 0.65f);
        // style.Colors[(int) ImGuiCol.TitleBgActive] = new Vector4(0.08f, 0.10f, 0.12f, 1.00f);
        // style.Colors[(int) ImGuiCol.TitleBgCollapsed] = new Vector4(0.00f, 0.00f, 0.00f, 0.51f);
        // style.Colors[(int) ImGuiCol.MenuBarBg] = new Vector4(0.15f, 0.18f, 0.22f, 1.00f);
        // style.Colors[(int) ImGuiCol.ScrollbarBg] = new Vector4(0.02f, 0.02f, 0.02f, 0.39f);
        // style.Colors[(int) ImGuiCol.ScrollbarGrab] = new Vector4(0.20f, 0.25f, 0.29f, 1.00f);
        // style.Colors[(int) ImGuiCol.ScrollbarGrabHovered] = new Vector4(0.18f, 0.22f, 0.25f, 1.00f);
        // style.Colors[(int) ImGuiCol.ScrollbarGrabActive] = new Vector4(0.09f, 0.21f, 0.31f, 1.00f);
        // style.Colors[(int) ImGuiCol.CheckMark] = new Vector4(0.28f, 0.56f, 1.00f, 1.00f);
        // style.Colors[(int) ImGuiCol.SliderGrab] = new Vector4(0.28f, 0.56f, 1.00f, 1.00f);
        // style.Colors[(int) ImGuiCol.SliderGrabActive] = new Vector4(0.37f, 0.61f, 1.00f, 1.00f);
        // style.Colors[(int) ImGuiCol.Button] = new Vector4(0.20f, 0.25f, 0.29f, 1.00f);
        // style.Colors[(int) ImGuiCol.ButtonHovered] = new Vector4(0.28f, 0.56f, 1.00f, 1.00f);
        // style.Colors[(int) ImGuiCol.ButtonActive] = new Vector4(0.06f, 0.53f, 0.98f, 1.00f);
        // style.Colors[(int) ImGuiCol.Header] = new Vector4(0.20f, 0.25f, 0.29f, 0.55f);
        // style.Colors[(int) ImGuiCol.HeaderHovered] = new Vector4(0.26f, 0.59f, 0.98f, 0.80f);
        // style.Colors[(int) ImGuiCol.HeaderActive] = new Vector4(0.26f, 0.59f, 0.98f, 1.00f);
        // style.Colors[(int) ImGuiCol.Separator] = new Vector4(0.20f, 0.25f, 0.29f, 1.00f);
        // style.Colors[(int) ImGuiCol.SeparatorHovered] = new Vector4(0.10f, 0.40f, 0.75f, 0.78f);
        // style.Colors[(int) ImGuiCol.SeparatorActive] = new Vector4(0.10f, 0.40f, 0.75f, 1.00f);
        // style.Colors[(int) ImGuiCol.ResizeGrip] = new Vector4(0.26f, 0.59f, 0.98f, 0.25f);
        // style.Colors[(int) ImGuiCol.ResizeGripHovered] = new Vector4(0.26f, 0.59f, 0.98f, 0.67f);
        // style.Colors[(int) ImGuiCol.ResizeGripActive] = new Vector4(0.26f, 0.59f, 0.98f, 0.95f);
        // style.Colors[(int) ImGuiCol.Tab] = new Vector4(0.11f, 0.15f, 0.17f, 1.00f);
        // style.Colors[(int) ImGuiCol.TabHovered] = new Vector4(0.26f, 0.59f, 0.98f, 0.80f);
        // style.Colors[(int) ImGuiCol.TabActive] = new Vector4(0.20f, 0.25f, 0.29f, 1.00f);
        // style.Colors[(int) ImGuiCol.TabUnfocused] = new Vector4(0.11f, 0.15f, 0.17f, 1.00f);
        // style.Colors[(int) ImGuiCol.TabUnfocusedActive] = new Vector4(0.11f, 0.15f, 0.17f, 1.00f);
        // style.Colors[(int) ImGuiCol.PlotLines] = new Vector4(0.61f, 0.61f, 0.61f, 1.00f);
        // style.Colors[(int) ImGuiCol.PlotLinesHovered] = new Vector4(1.00f, 0.43f, 0.35f, 1.00f);
        // style.Colors[(int) ImGuiCol.PlotHistogram] = new Vector4(0.90f, 0.70f, 0.00f, 1.00f);
        // style.Colors[(int) ImGuiCol.PlotHistogramHovered] = new Vector4(1.00f, 0.60f, 0.00f, 1.00f);
        // style.Colors[(int) ImGuiCol.TextSelectedBg] = new Vector4(0.26f, 0.59f, 0.98f, 0.35f);
        // style.Colors[(int) ImGuiCol.DragDropTarget] = new Vector4(1.00f, 1.00f, 0.00f, 0.90f);
        // style.Colors[(int) ImGuiCol.NavHighlight] = new Vector4(0.26f, 0.59f, 0.98f, 1.00f);
        // style.Colors[(int) ImGuiCol.NavWindowingHighlight] = new Vector4(1.00f, 1.00f, 1.00f, 0.70f);
        // style.Colors[(int) ImGuiCol.NavWindowingDimBg] = new Vector4(0.80f, 0.80f, 0.80f, 0.20f);
        // style.Colors[(int) ImGuiCol.ModalWindowDimBg] = new Vector4(0.80f, 0.80f, 0.80f, 0.35f);
    }

    public void Update(float deltaTime) => _controller.Update(deltaTime);
    public void Render() => _controller.Render();
    public void Dispose() => _controller.Dispose();
}
