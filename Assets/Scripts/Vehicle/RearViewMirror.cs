using UnityEngine;
using UnityEngine.UI;

namespace LocalFormulaRacing
{
    /// <summary>
    /// Always-on rear-view mirror pinned to the top-centre of the screen in
    /// every camera angle (per request - it replaces the dedicated rear-facing
    /// camera angle, which became redundant). A second camera rides the car
    /// looking backwards over the engine cover into a small render texture,
    /// which the HUD shows horizontally flipped - left stays left, exactly
    /// like a real mirror. Attached by CameraRig, so it works identically
    /// under both the Cinemachine and legacy camera backends.
    /// </summary>
    public class RearViewMirror : MonoBehaviour
    {
        // Wide-strip mirror: ~3.3:1, like the broadcast/game convention.
        const int TextureWidth = 640;
        const int TextureHeight = 192;

        // On-screen size at the 1920x1080 reference resolution.
        const float ScreenWidth = 520f;
        const float ScreenHeight = 156f;
        const float FrameThickness = 4f;

        Transform car;
        Camera mirrorCamera;
        RenderTexture mirrorTexture;
        Canvas canvas;

        public static RearViewMirror Attach(Transform car, Transform parent)
        {
            if (car == null)
            {
                return null;
            }

            // Re-initialising an existing rig (session restart) must not stack
            // a second mirror camera/canvas on top of the first.
            if (parent != null)
            {
                RearViewMirror existing = parent.GetComponentInChildren<RearViewMirror>(true);
                if (existing != null)
                {
                    Destroy(existing.gameObject);
                }
            }

            var mirrorObject = new GameObject("Rear-view mirror");
            if (parent != null)
            {
                // Riding under the camera rig ties the mirror's lifetime to the
                // race camera itself: session teardown destroys the rig and the
                // mirror (camera, RT, canvas) goes with it via OnDestroy below.
                mirrorObject.transform.SetParent(parent, false);
            }

            var mirror = mirrorObject.AddComponent<RearViewMirror>();
            mirror.Build(car);
            return mirror;
        }

        void Build(Transform followTarget)
        {
            car = followTarget;

            mirrorTexture = new RenderTexture(TextureWidth, TextureHeight, 16)
            {
                name = "Rear-view mirror RT"
            };

            var cameraObject = new GameObject("Rear-view mirror camera");
            cameraObject.transform.SetParent(transform, false);
            mirrorCamera = cameraObject.AddComponent<Camera>();
            mirrorCamera.targetTexture = mirrorTexture;
            // Vertical FOV on a 3.3:1 strip works out to a usefully wide
            // (~80 degree) horizontal sweep - a car diving to either side to
            // overtake stays in the mirror until it's genuinely alongside.
            mirrorCamera.fieldOfView = 30f;
            mirrorCamera.nearClipPlane = 0.3f;
            // The mirror is for racing behind you, not the horizon - a short
            // far plane keeps the second render cheap.
            mirrorCamera.farClipPlane = 1200f;
            mirrorCamera.allowHDR = false;
            mirrorCamera.allowMSAA = false;
            mirrorCamera.backgroundColor = RenderSettings.fogColor;
            int uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer >= 0)
            {
                mirrorCamera.cullingMask &= ~(1 << uiLayer);
            }

            BuildHud();
            SyncToCar();
        }

        void BuildHud()
        {
            canvas = UiFactory.CreateCanvas("Rear-view mirror canvas");

            // Thin dark surround so the feed reads as a mirror housing rather
            // than raw video floating over the scene.
            var frameObject = new GameObject("Mirror frame");
            frameObject.transform.SetParent(canvas.transform, false);
            var frameRect = frameObject.AddComponent<RectTransform>();
            frameRect.anchorMin = new Vector2(0.5f, 1f);
            frameRect.anchorMax = new Vector2(0.5f, 1f);
            frameRect.pivot = new Vector2(0.5f, 1f);
            frameRect.anchoredPosition = new Vector2(0f, -10f);
            frameRect.sizeDelta = new Vector2(ScreenWidth + FrameThickness * 2f, ScreenHeight + FrameThickness * 2f);
            var frameImage = frameObject.AddComponent<Image>();
            frameImage.color = new Color(0.03f, 0.03f, 0.04f, 0.88f);
            frameImage.raycastTarget = false;

            var viewObject = new GameObject("Mirror view");
            viewObject.transform.SetParent(frameObject.transform, false);
            var viewRect = viewObject.AddComponent<RectTransform>();
            viewRect.anchorMin = Vector2.zero;
            viewRect.anchorMax = Vector2.one;
            viewRect.offsetMin = new Vector2(FrameThickness, FrameThickness);
            viewRect.offsetMax = new Vector2(-FrameThickness, -FrameThickness);
            var view = viewObject.AddComponent<RawImage>();
            view.texture = mirrorTexture;
            // Horizontal flip: a camera pointed backwards swaps left and right
            // relative to the driver; mirroring the U axis puts a car on your
            // left on the LEFT of the mirror, the way a real mirror behaves.
            view.uvRect = new Rect(1f, 0f, -1f, 1f);
            view.raycastTarget = false;
        }

        void LateUpdate()
        {
            if (car == null)
            {
                // Car despawned (session teardown order isn't guaranteed) -
                // take the mirror down rather than showing a frozen frame.
                Destroy(gameObject);
                return;
            }

            SyncToCar();
        }

        void SyncToCar()
        {
            // Hard-mounted above the airbox looking straight back with a
            // slight downward cast, so the frame reads own-rear-wing at the
            // bottom with the chasing cars above it. Uses the car's own up
            // vector so the mirror banks with the car like real mirrors do.
            mirrorCamera.transform.position = car.TransformPoint(new Vector3(0f, 1.35f, -0.1f));
            Vector3 lookDirection = -car.forward + car.up * -0.06f;
            if (lookDirection.sqrMagnitude < 0.001f)
            {
                lookDirection = -car.forward;
            }

            mirrorCamera.transform.rotation = Quaternion.LookRotation(lookDirection, car.up);

            // Weather can transition mid-session; keep the mirror's fallback
            // clear colour in step with the scene fog the same way the main
            // camera does.
            mirrorCamera.backgroundColor = RenderSettings.fogColor;
        }

        void OnDestroy()
        {
            if (canvas != null)
            {
                Destroy(canvas.gameObject);
            }

            if (mirrorTexture != null)
            {
                if (mirrorCamera != null)
                {
                    mirrorCamera.targetTexture = null;
                }

                mirrorTexture.Release();
                Destroy(mirrorTexture);
            }
        }
    }
}
