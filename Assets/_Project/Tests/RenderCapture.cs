#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using FPS.Locomotion;
using FPS.Weapons;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace FPS.Tests
{
    public class RenderCapture : InputTestFixture
    {
        [UnityTest]
        public IEnumerator CaptureHipAdsCanted()
        {
            string dir = Environment.GetEnvironmentVariable("FPS_CAPTURE_DIR");
            if (string.IsNullOrEmpty(dir)) Assert.Ignore("FPS_CAPTURE_DIR not set");
            var keyboard = InputSystem.AddDevice<Keyboard>();
            var mouse = InputSystem.AddDevice<Mouse>();

            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/_Project/Scenes/LocomotionTestRange.unity",
                new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;

            var rig = Object.FindAnyObjectByType<PlayerRig>();
            var cc = rig.GetComponent<CharacterController>();
            cc.enabled = false;
            rig.transform.SetPositionAndRotation(new Vector3(20f, 0f, 2f), Quaternion.identity);
            cc.enabled = true;

            var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            rig.PlayerCamera.targetTexture = rt;
            yield return new WaitForSeconds(2.5f);
            Save(rt, Path.Combine(dir, "hip.png"));

            Press(mouse.rightButton);
            yield return new WaitForSeconds(1.5f);
            Save(rt, Path.Combine(dir, "ads.png"));
            var ads = rig.GetComponent<ProceduralADSAndOpticsController>();
            Debug.Log($"[Capture] blend={ads.AdsBlend} scopeEnabled={ads.ScopeCamera.enabled} scopeFov={ads.ScopeCamera.fieldOfView} mainFov={rig.PlayerCamera.fieldOfView}");
            SaveHdr(ads.ScopeCamera.targetTexture, Path.Combine(dir, "scope.png"));

            Press(keyboard.xKey);
            Release(keyboard.xKey);
            yield return new WaitForSeconds(1f);
            Save(rt, Path.Combine(dir, "canted.png"));
            rig.PlayerCamera.targetTexture = null;
        }

        private static void Save(RenderTexture rt, string path)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;
            var texture = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            texture.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            texture.Apply();
            RenderTexture.active = previous;
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.Destroy(texture);
        }

        private static void SaveHdr(RenderTexture rt, string path)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;
            var texture = new Texture2D(rt.width, rt.height, TextureFormat.RGBAHalf, false, true);
            texture.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            texture.Apply();
            RenderTexture.active = previous;
            Color[] pixels = texture.GetPixels();
            float max = 0f;
            for (int i = 0; i < pixels.Length; i++) max = Mathf.Max(max, pixels[i].maxColorComponent);
            Debug.Log($"[Capture] scope max={max}");
            var output = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            for (int i = 0; i < pixels.Length; i++)
            {
                Color c = pixels[i];
                c = new Color(c.r / (1f + c.r), c.g / (1f + c.g), c.b / (1f + c.b), 1f).gamma;
                pixels[i] = c;
            }
            output.SetPixels(pixels);
            output.Apply();
            File.WriteAllBytes(path, output.EncodeToPNG());
        }
    }
}
#endif
