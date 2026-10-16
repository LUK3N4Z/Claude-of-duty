using System;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace FPS.EditorTools
{
    [Serializable]
    internal sealed class MeshyPreviewRequest
    {
        public string mode = "preview";
        public string prompt;
        public string art_style = "realistic";
        public string ai_model = "latest";
        public string topology = "triangle";
        public int target_polycount = 15000;
        public bool should_remesh = true;
        public string[] target_formats = { "fbx", "glb" };
    }

    [Serializable]
    internal sealed class MeshyRefineRequest
    {
        public string mode = "refine";
        public string preview_task_id;
        public bool enable_pbr = true;
        public string texture_prompt;
        public string texture_resolution = "2k";
        public string[] target_formats = { "fbx", "glb" };
    }

    [Serializable]
    internal sealed class MeshyCreateResponse
    {
        public string result;
    }

    [Serializable]
    internal sealed class MeshyModelUrls
    {
        public string glb;
        public string fbx;
        public string obj;
        public string usdz;
    }

    [Serializable]
    internal sealed class MeshyTextureUrls
    {
        public string base_color;
        public string metallic;
        public string normal;
        public string roughness;
        public string emission;
    }

    [Serializable]
    internal sealed class MeshyTaskError
    {
        public string message;
    }

    [Serializable]
    internal sealed class MeshyTask
    {
        public string id;
        public string type;
        public string status;
        public int progress;
        public MeshyModelUrls model_urls;
        public MeshyTextureUrls[] texture_urls;
        public string thumbnail_url;
        public MeshyTaskError task_error;
        public int consumed_credits;

        public bool Succeeded => status == "SUCCEEDED";
        public bool Finished => status == "SUCCEEDED" || status == "FAILED" || status == "CANCELED";
    }

    /// <summary>
    /// Minimal client for Meshy's Text to 3D REST API (POST /openapi/v2/text-to-3d for preview and refine tasks, GET for
    /// status, then plain downloads of the result files). Requests carry the key as a Bearer token. API usage is billed
    /// in Meshy API credits, separately from the meshy.ai web subscription.
    /// </summary>
    internal sealed class MeshyApiClient
    {
        public const string Endpoint = "https://api.meshy.ai/openapi/v2/text-to-3d";
        public const string ApiKeyEnvironmentVariable = "MESHY_API_KEY";
        public const string ApiKeyPreference = "FPS.MeshyApiKey";

        private readonly string apiKey;

        public MeshyApiClient(string key)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("A Meshy API key is required.", nameof(key));
            apiKey = key;
        }

        /// <summary>The key from MESHY_API_KEY, else the editor preference; empty when neither is set.</summary>
        public static string ResolveApiKey()
        {
            string key = Environment.GetEnvironmentVariable(ApiKeyEnvironmentVariable);
            return !string.IsNullOrEmpty(key) ? key : UnityEditor.EditorPrefs.GetString(ApiKeyPreference, string.Empty);
        }

        public static string PreviewJson(MeshyAssetSpec spec) =>
            JsonUtility.ToJson(new MeshyPreviewRequest { prompt = Clip(spec.Prompt), target_polycount = spec.Polycount });

        public static string RefineJson(string previewTaskId, MeshyAssetSpec spec) =>
            JsonUtility.ToJson(new MeshyRefineRequest { preview_task_id = previewTaskId, texture_prompt = Clip(spec.TexturePrompt) });

        public static MeshyTask ParseTask(string json) => JsonUtility.FromJson<MeshyTask>(json);

        public static string ParseCreated(string json) => JsonUtility.FromJson<MeshyCreateResponse>(json)?.result;

        /// <summary>Submits a task body (preview or refine) and returns the new task id.</summary>
        public async Task<string> CreateAsync(string json)
        {
            using (var request = new UnityWebRequest(Endpoint, UnityWebRequest.kHttpVerbPOST))
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                Authorize(request);
                await Send(request);
                string id = ParseCreated(request.downloadHandler.text);
                if (string.IsNullOrEmpty(id)) throw new InvalidOperationException($"Meshy returned no task id: {request.downloadHandler.text}");
                return id;
            }
        }

        public async Task<MeshyTask> GetAsync(string taskId)
        {
            using (UnityWebRequest request = UnityWebRequest.Get($"{Endpoint}/{taskId}"))
            {
                Authorize(request);
                await Send(request);
                return ParseTask(request.downloadHandler.text);
            }
        }

        /// <summary>Downloads a result file (signed URL, no auth header) to <paramref name="fullPath"/>.</summary>
        public static async Task DownloadAsync(string url, string fullPath)
        {
            using (var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbGET))
            {
                request.downloadHandler = new DownloadHandlerFile(fullPath) { removeFileOnAbort = true };
                await Send(request);
            }
        }

        private void Authorize(UnityWebRequest request) => request.SetRequestHeader("Authorization", $"Bearer {apiKey}");

        private static async Task Send(UnityWebRequest request)
        {
            var completion = new TaskCompletionSource<bool>();
            UnityWebRequestAsyncOperation operation = request.SendWebRequest();
            operation.completed += _ => completion.TrySetResult(true);
            await completion.Task;
            if (request.result != UnityWebRequest.Result.Success)
            {
                string body = request.downloadHandler is DownloadHandlerBuffer buffer ? buffer.text : string.Empty;
                throw new InvalidOperationException($"Meshy request {request.method} {request.url} failed ({request.responseCode} {request.error}) {body}");
            }
        }

        private static string Clip(string text) => text != null && text.Length > 800 ? text.Substring(0, 800) : text ?? string.Empty;
    }
}
