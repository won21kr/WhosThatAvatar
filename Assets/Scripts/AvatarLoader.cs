﻿#define TEST_DECOMPRESS //comment this out in build. This is to test the WebGL decompressor.
//#define TEST_LOAD_ON_START

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using VRCAvatarAssetbundleDecompressor;

public class AvatarLoader : MonoBehaviour
{
    public static AvatarLoader instance;
    private AssetBundle loadedAssetBundle;
    private GameObject instantiatedAvatar;
    private MouseOrbitImproved mouseOrbit;
    bool abort = false;
    private byte[] buff;

    private bool CheckedQuerstring = false;

    //Unity 5.5 and later does not support extracting an assetbundle compressed with LZMA in WebGL: https://blogs.unity3d.com/cn/2016/09/20/understanding-memory-in-unity-webgl/
    //we have to manually decompress and rebuild blocks and metadata. LZMA decompress block contents.    
    //also, appearently unity wont open assetbundles built for another version, even if is able to do so.
    //see Decompressor.Attempt for how this is done.

    private void Awake()
    {
        if (instance != null)
        {
            Destroy(instance);
        }
        instance = this;
        mouseOrbit = GameObject.FindObjectOfType<MouseOrbitImproved>();
    }


    void Start ()
    {
    }

    private void CheckQuerystringParameters()
    {
        var avatarQuerystring = "";
#if UNITY_WEBGL && !UNITY_EDITOR
        avatarQuerystring = WebpageUtilities.GetURLParameters();
#elif TEST_LOAD_ON_START
        avatarQuerystring = "avtr_53411a6e-8b3a-486e-983a-9249d7b6a087";
#endif
        if (!string.IsNullOrEmpty(avatarQuerystring))
        {
            LoadAvatar(avatarQuerystring);
        }
    }

    void PrintOrReplaceShaders(GameObject go, bool shared = false, bool replace = false)
    {
        if (go)
        {
            Renderer[] componentsInChildren = go.GetComponentsInChildren<Renderer>(true);
            List<Renderer> list = new List<Renderer>();
            list.AddRange(componentsInChildren);
            Debug.Log(list.Count.ToString() + " total shaders");
            int replacedShaderCount = 0;
            for (int i = 0; i < list.Count; i++)
            {
                Material[] materials;
                if (!shared)
                    materials = list[i].materials;
                else
                    materials = list[i].sharedMaterials;

                for (int j = 0; j < materials.Length; j++)
                {
                    Material material = materials[j];
                    string name = material.shader.name;
                    if (replace)
                    {
                        var foundShader = Shader.Find(name);
                        replacedShaderCount++;
                        //check if the shader loaded from the assetbundle, if not use a proxy shader
                        if (foundShader != null)
                        {
                            material.shader = foundShader;
                        }
                        else
                        {
                            if (SettingsManager.instance.ReplacementShader.name.Contains("ProxyShaderNone"))
                            {
                                //will probably just be pink
                            }
                            else
                            {
                                material.shader = SettingsManager.instance.ReplacementShader;
                                Debug.Log("Shader " + name + " could not be found and was replaced with " + SettingsManager.instance.ReplacementShader.name);
                            }
                        }                        
                    }
                    else
                    {
                        Debug.Log("Found shader: " + name);
                    }
                }
            }
            if (replace)
                Debug.Log("Replaced " + replacedShaderCount.ToString() + " shaders.");
        }
    }
    void Update ()
    {
        if (!CheckedQuerstring && Time.timeSinceLevelLoad > 0.5f)
        {
            CheckedQuerstring = true;
            CheckQuerystringParameters();
        }  
    }

    public void LoadAvatar(string avatarID)
    {
        //todo: Disable ui?
        //todo: cancel button
        try
        {
            UIManager.instance.SetMainTitle("Loading avatar", "Getting avatar info...");
            StartCoroutine(VRCAPIHandler.GetAvatarInfo(avatarID, (avatarInfo) =>
            {
                if (avatarInfo.releaseStatus != "public")
                {
                    UIManager.instance.SetMainTitle(null, "This avatar is not public and cannot be viewed.");
                    return;
                }

                UIManager.instance.SetMainTitle(avatarInfo.name, "By: " + avatarInfo.authorName);
                StartCoroutine(DownloadAvatar(VRCAPIHandler.CheckProxyUrl(avatarInfo.assetUrl), () =>
                {
                    StartCoroutine(InstantiateAvatar(() =>
                    {
                        UIManager.instance.SetMainTitle(avatarInfo.name, "By: " + avatarInfo.authorName);
                        UIManager.instance.SetLoadedAvatarId(avatarInfo.id);

                        GameObject mouseOrbitFollow = new GameObject("MouseOrbitFollow");
                        //mouseOrbitFollow.transform.position = instantiatedAvatar.GetComponentInChildren<Renderer>().bounds.center;
                        mouseOrbitFollow.transform.position = Vector3.zero;

                        mouseOrbit.target = mouseOrbitFollow.transform;
                        if (avatarInfo.name == null)
                            avatarInfo.name = "";
                        if (avatarInfo.authorName == null)
                            avatarInfo.authorName = "?";

                        WebpageUtilities.SetURLParameters(avatarInfo.id);
                        //todo: enable UI
                    }));
                }));
            }, (error) =>
            {
                UIManager.instance.SetMainTitle(null, "Could not find that avatar");
            }));
        }
        catch(Exception e)
        {
            Debug.Log(e.ToString());
        }
    }

    private IEnumerator GetWebData(string url, Action<byte[]> response)
    {
        using (UnityWebRequest www = UnityWebRequest.Get(url))
        {
            www.Send();
            Debug.Log("Downloading...");
            while (!www.isDone && !abort)
            {
                UIManager.instance.SetMainLoadingBarProgress(www.downloadProgress);
                yield return null;
            }
            response(www.downloadHandler.data);
        }
    }

    public IEnumerator DownloadAvatar(string url, Action onDownloaded)
    {
        bool errorShown = false;
        Debug.Log("Downloading avatar from: " + url);
        abort = false;
        bool success = false;
        try
        {
            if (instantiatedAvatar)
            {
                Debug.Log("Destroying previous loaded avatar...");
                Destroy(instantiatedAvatar);
            }
            if (loadedAssetBundle != null)
            {
                Debug.Log("Unloading previous assetbundle...");
                try
                {
                    loadedAssetBundle.Unload(true);
                }
                catch
                {
                    Debug.Log("Error unloading previous assetbundle...");
                }
            }

            UIManager.instance.SetMainTitle(null, "Downloading...");

            var downloaded = false;
            byte[] data = null;
            StartCoroutine(GetWebData(url,(resp)=>
                {
                    data = resp;
            }));            
            while (data == null && !abort)
            {               
                yield return null;
            }
            Debug.Log("File downloaded");
            if (abort)
            {
                Debug.Log("Aborting from download...");
                UIManager.instance.SetMainLoadingBarProgress(1f);
                UIManager.instance.SetMainTitle("", "Cancelled");
            }
            else
            {
                if (data.Length < 200)
                {
                    var dataString = Encoding.UTF8.GetString(data);
                    Debug.Log(dataString);
                    if (dataString.StartsWith("https"))
                    {
                        Debug.Log("File was redirected.");
                        data = null;
                        StartCoroutine(GetWebData(dataString, (resp) =>
                        {
                            data = resp;
                        }));
                        while (data == null && !abort)
                        {
                            yield return null;
                        }

                    }
                    else
                    {
                        UIManager.instance.SetMainTitle("Error", "API Error. File may be too large.");
                        errorShown = true;
                    }
                }

                if (data.Length >= 200)
                {
                    Debug.Log("Decompressing, client may hang...");
                    yield return new WaitForEndOfFrame();

                    //This is only needed for WebGL. Otherwise just use the assetbundle how you would any other.
                    byte[] assetbundleData = null;

#if UNITY_WEBGL && !UNITY_EDITOR || TEST_DECOMPRESS
                    assetbundleData = Decompressor.Attempt(data);
#else
                    assetbundleData = data;
#endif
                    Debug.Log("Loading assetbundle async...");
                    yield return new WaitForEndOfFrame();
                    var request = AssetBundle.LoadFromMemoryAsync(assetbundleData);
                    yield return new WaitForEndOfFrame();
                    while (!request.isDone)
                    {
                        yield return new WaitForEndOfFrame();
                    }
                    loadedAssetBundle = request.assetBundle;
                    success = true;
                }
            }
        }
        finally
        {
            if (!success)
            {
                if (!errorShown)
                {
                    if (!abort)
                    {
                        UIManager.instance.SetMainTitle("Error", "Could not load avatar!");
                        Debug.Log("Coulden't load avatar");
                    }
                    else
                    {
                        UIManager.instance.SetMainTitle("Error", "Aborted!");
                        Debug.Log("Aborted!");
                    }
                }
                
            }
            else
            {
                onDownloaded();
            }
        }
    }
    public IEnumerator InstantiateAvatar( Action onInstantiated)
    {
        if (loadedAssetBundle == null)
        {
            UIManager.instance.SetMainTitle("Error", "Could not load avatar!");
            yield break;
        }
        UIManager.instance.SetMainTitle(null, "Loading...");
        var request = loadedAssetBundle.LoadAssetWithSubAssetsAsync("assets/_customavatar.prefab");
        while(!request.isDone && !abort)
        {
            Debug.Log("loading " + request.progress);//comment this out in build
            UIManager.instance.SetMainLoadingBarProgress(request.progress);
            yield return null;
        }
        if (abort)
        {
            Debug.Log("Aborting from instantiation...");
            request = null;
        }
        else
        {
            UIManager.instance.SetMainTitle(null, "Instantiating...");

            if ((request.asset as GameObject).transform.FindChild("WTA_IGNORE") != null)
            {
                Debug.Log("found WTA_IGNORE. Aborting.");
                UIManager.instance.SetMainTitle("Error", "This avatar is not public.");
                request = null;
                yield break;
            }

            instantiatedAvatar = Instantiate(request.asset) as GameObject;
            instantiatedAvatar.transform.position = Vector3.zero;

            var bounds = instantiatedAvatar.GetComponentInChildren<Renderer>().bounds.center;
            instantiatedAvatar.transform.position = new Vector3(bounds.x, instantiatedAvatar.transform.position.y, bounds.z);

            //WebGL has issues with alot of shaders.
#if UNITY_WEBGL && !UNITY_EDITOR
            PrintOrReplaceShaders(instantiatedAvatar, false, true);
#endif

            if (instantiatedAvatar.GetComponent<Animator>())
            {
                instantiatedAvatar.GetComponent<Animator>().runtimeAnimatorController = GameObject.FindObjectOfType<ObjectHolder>().animationController;
            }

            UIManager.instance.SetMainLoadingBarProgress(1f);
            //UIManager.instance.SetStatusText();

            onInstantiated();
        }
    }

    //for testing with predownloaded assetbundles
    public void LoadAssetBundleFromStreamingFolder(string filename)
    {
        TextAsset asset = Resources.Load(filename) as TextAsset;
        loadedAssetBundle = AssetBundle.LoadFromMemory(asset.bytes);

        if (loadedAssetBundle == null)
        {
            Debug.Log("Failed to load AssetBundle!");
            return;
        }
        var request = loadedAssetBundle.LoadAssetWithSubAssets("assets/_customavatar.prefab");

        instantiatedAvatar = Instantiate(request[0]) as GameObject;
        instantiatedAvatar.transform.position = Vector3.zero;
        
        GameObject mouseOrbitFollow = new GameObject("MouseOrbitFollow");
        mouseOrbitFollow.transform.position = instantiatedAvatar.GetComponentInChildren<Renderer>().bounds.center;
        mouseOrbit.target = mouseOrbitFollow.transform;    
    }

    public byte[] LoadFile(string filename)
    {
        var path = Application.dataPath + "/StreamingAssets/" + filename;
        return File.ReadAllBytes(path);
    }

    [Serializable]
    public class error
    {
        public string message;
        public int status_code;
    }
   
}
