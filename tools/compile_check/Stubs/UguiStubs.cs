// Compile-check stubs for the uGUI package (com.unity.ugui). Only the members the game uses.
// These mirror the real public API signatures; they are NOT shipped with the game.
using System;
using UnityEngine.Events;

namespace UnityEngine.EventSystems
{
    public class UIBehaviour : MonoBehaviour { }
    public class BaseEventData { }
    public class PointerEventData : BaseEventData { }
    public interface IEventSystemHandler { }
    public interface IPointerEnterHandler : IEventSystemHandler { void OnPointerEnter(PointerEventData eventData); }
    public interface IPointerExitHandler : IEventSystemHandler { void OnPointerExit(PointerEventData eventData); }
    public class EventSystem : UIBehaviour
    {
        public static EventSystem current { get; set; }
        public bool IsPointerOverGameObject() => false;
    }
    public abstract class BaseInputModule : UIBehaviour { }
    public abstract class PointerInputModule : BaseInputModule { }
    public class StandaloneInputModule : PointerInputModule { }
    public abstract class BaseRaycaster : UIBehaviour { }
}

namespace UnityEngine.UI
{
    using UnityEngine.EventSystems;

    public abstract class Graphic : UIBehaviour
    {
        public virtual Color color { get; set; }
        public virtual Material material { get; set; }
        public bool raycastTarget { get; set; }
        public RectTransform rectTransform => null;
    }
    public abstract class MaskableGraphic : Graphic { }
    public class Image : MaskableGraphic
    {
        public enum Type { Simple, Sliced, Tiled, Filled }
        public enum FillMethod { Horizontal, Vertical, Radial90, Radial180, Radial360 }
        public Sprite sprite { get; set; }
        public Type type { get; set; }
        public bool preserveAspect { get; set; }
        public FillMethod fillMethod { get; set; }
        public float fillAmount { get; set; }
        public float pixelsPerUnitMultiplier { get; set; }
    }
    public class RawImage : MaskableGraphic
    {
        public Texture texture { get; set; }
        public Rect uvRect { get; set; }
    }
    public class Text : MaskableGraphic
    {
        public Font font { get; set; }
        public int fontSize { get; set; }
        public TextAnchor alignment { get; set; }
        public virtual string text { get; set; }
        public HorizontalWrapMode horizontalOverflow { get; set; }
        public VerticalWrapMode verticalOverflow { get; set; }
        public bool supportRichText { get; set; }
    }
    public class Selectable : UIBehaviour
    {
        public enum Transition { None, ColorTint, SpriteSwap, Animation }
        public Transition transition { get; set; }
    }
    public class Button : Selectable
    {
        [Serializable] public class ButtonClickedEvent : UnityEvent { }
        public ButtonClickedEvent onClick { get; set; }
    }
    public class CanvasScaler : UIBehaviour
    {
        public enum ScaleMode { ConstantPixelSize, ScaleWithScreenSize, ConstantPhysicalSize }
        public enum ScreenMatchMode { MatchWidthOrHeight, Expand, Shrink }
        public ScaleMode uiScaleMode { get; set; }
        public Vector2 referenceResolution { get; set; }
        public ScreenMatchMode screenMatchMode { get; set; }
        public float matchWidthOrHeight { get; set; }
    }
    public class GraphicRaycaster : BaseRaycaster { }
    public abstract class BaseMeshEffect : UIBehaviour { }
    public class Shadow : BaseMeshEffect
    {
        public Color effectColor { get; set; }
        public Vector2 effectDistance { get; set; }
    }
    public abstract class LayoutGroup : UIBehaviour
    {
        public RectOffset padding { get; set; }
        public TextAnchor childAlignment { get; set; }
    }
    public abstract class HorizontalOrVerticalLayoutGroup : LayoutGroup
    {
        public float spacing { get; set; }
        public bool childForceExpandWidth { get; set; }
        public bool childForceExpandHeight { get; set; }
        public bool childControlWidth { get; set; }
        public bool childControlHeight { get; set; }
    }
    public class VerticalLayoutGroup : HorizontalOrVerticalLayoutGroup { }
    public class HorizontalLayoutGroup : HorizontalOrVerticalLayoutGroup { }
    public class LayoutElement : UIBehaviour
    {
        public virtual float preferredWidth { get; set; }
        public virtual float preferredHeight { get; set; }
        public virtual float minHeight { get; set; }
        public virtual float flexibleWidth { get; set; }
    }
    public class ContentSizeFitter : UIBehaviour
    {
        public enum FitMode { Unconstrained, MinSize, PreferredSize }
        public FitMode verticalFit { get; set; }
        public FitMode horizontalFit { get; set; }
    }
    public class ScrollRect : UIBehaviour
    {
        public enum MovementType { Unrestricted, Elastic, Clamped }
        public bool horizontal { get; set; }
        public bool vertical { get; set; }
        public MovementType movementType { get; set; }
        public float scrollSensitivity { get; set; }
        public RectTransform content { get; set; }
        public RectTransform viewport { get; set; }
    }
    public class RectMask2D : UIBehaviour { }
    public class AspectRatioFitter : UIBehaviour
    {
        public enum AspectMode { None, WidthControlsHeight, HeightControlsWidth, FitInParent, EnvelopeParent }
        public AspectMode aspectMode { get; set; }
        public float aspectRatio { get; set; }
    }
}
