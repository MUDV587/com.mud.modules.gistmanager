using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace MUD.Modules.GistManager.Components
{
    /// <summary>
    /// Splitter 控制器：左右分栏可拖分割线。
    /// 左 pane 按绝对像素设置 width（300px 起，钳制到窗口宽度 - 右栏最小 300px - 3px 手柄，可尽量贴近右沿）；
    /// 右 pane flexGrow=1 填满剩余空间。
    /// </summary>
    public sealed class SplitterController
    {
        private readonly VisualElement Root;
        private readonly VisualElement? FirstPane;
        private readonly VisualElement? SecondPane;
        private readonly VisualElement Handle;

        private bool _dragging;

        public SplitterController(VisualElement root)
        {
            Root = root ?? throw new ArgumentNullException(nameof(root));
            FirstPane = root.Q<VisualElement>("leftColumnContainer");
            SecondPane = root.Q<VisualElement>("rightColumnContainer");
            Handle = root.Q<VisualElement>("SplitterHandle")
                ?? throw new InvalidOperationException("缺少元素 'SplitterHandle'。");

            // UI Toolkit 按 legacy Mouse 派发（Editor 默认进来的是 MouseDown/Move/Up）
            Handle.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button != 0 || FirstPane == null || SecondPane == null)
                    return;
                evt.StopPropagation();
                _dragging = true;
                Handle.CaptureMouse();
            });
            Handle.RegisterCallback<MouseMoveEvent>(OnMouseMove);
            Handle.RegisterCallback<MouseUpEvent>(_ => EndDrag());
            Handle.RegisterCallback<MouseCaptureOutEvent>(_ => EndDrag());

            // new Pointer events 也兜底（runtime / UI Builder 的新输入走 Pointer*）
            Handle.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0 || FirstPane == null || SecondPane == null)
                    return;
                evt.StopPropagation();
                _dragging = true;
                Handle.CaptureMouse();
                Handle.CapturePointer(UnityEngine.UIElements.PointerId.mousePointerId);
            });
            Handle.RegisterCallback<PointerMoveEvent>(evt => DragTo(evt.position.x));
            Handle.RegisterCallback<PointerUpEvent>(_ => EndDrag());
            Handle.RegisterCallback<PointerCancelEvent>(_ => EndDrag());
            Handle.RegisterCallback<PointerCaptureOutEvent>(_ => EndDrag());
        }

        private void OnMouseMove(MouseMoveEvent evt)
        {
            if (!_dragging || FirstPane == null || SecondPane == null)
                return;
            DragTo(evt.mousePosition.x);
        }

        private void DragTo(float positionX)
        {
            if (!_dragging || FirstPane == null || SecondPane == null)
                return;

            var width = Root.layout.width;
            if (width <= 0f)
                return;

            // 左栏绝对宽度（px）= 鼠标位置；钳制到 300..(窗口宽度 - 右栏最小300px - 3px手柄)，尽量贴近右沿
            var rootX = Root.worldBound.x;
            var handleWidth = Handle.resolvedStyle.width;
            if (float.IsNaN(handleWidth) || handleWidth <= 0f)
                handleWidth = 3f;

            var maxByWindow = width - 300f - handleWidth;
            var px = Mathf.Clamp(positionX - rootX, 300f, Mathf.Min(4096f, Mathf.Max(300f, maxByWindow)));
            FirstPane.style.width = new StyleLength(px);
        }

        private void EndDrag()
        {
            if (!_dragging)
                return;

            _dragging = false;
            Handle.ReleaseMouse();
            Handle.ReleasePointer(UnityEngine.UIElements.PointerId.mousePointerId);
        }
    }
}
