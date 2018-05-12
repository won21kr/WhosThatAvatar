﻿using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class SearchManager : MonoBehaviour
{
    public static SearchManager instance;

    private GameObject SearchCanvas;
    private Button FindAvatarButton;
    //private Button BrowseAvatarsButton;

    //search
    private InputField SearchInput;
    private Toggle DownloadImages;
    private Dropdown SortDropdown;
    private Dropdown OrderDropdown;
    private InputField PageInput;
    private Toggle HasKnownLocationToggle;
    private Button SearchButton;
    private Button CloseButton;

    private RectTransform ResultsContent;
    private Button NextPageButton;
    private Button PreviousPageButton;

    public LoadingObject loadingObject;

    private int page = 0;

    //layout
    private GameObject SearchResultLayout;
    private RawImage ThumbnailImage;
    private Text NameText;
    private Text DescriptionText;
    private Button LoadButton;
    private Text LocationText;

    private List<VRCAPIHandler.AvatarListItem> avatarSearchResults;
    private List<GameObject> avatarSearchGameobjects;

    private float resultsContentOriginalHeight = 100f;
    public bool IsActive = false;


    // Use this for initialization
    void Start ()
    {
        instance = this;
        //Todo: Make a method to get these.
        avatarSearchGameobjects = new List<GameObject>();

        SearchCanvas = GameObject.Find("SearchCanvas");
        FindAvatarButton = GameObject.Find("FindAnAvatarButton").GetComponent<Button>();
        FindAvatarButton.onClick.AddListener(() =>
        {
            CloseSearchWindow(true);
        });
        //BrowseAvatarsButton = GameObject.Find("BrowseAvatarsButton").GetComponent<Button>();
        /* BrowseAvatarsButton.onClick.AddListener(() =>
        {
            CloseSearchWindow(true);
        });
        */
        SearchInput = GameObject.Find("SearchInput").GetComponent<InputField>();
        DownloadImages = GameObject.Find("DownloadImages").GetComponent<Toggle>();
        SortDropdown = GameObject.Find("SortDropdown").GetComponent<Dropdown>();
        OrderDropdown = GameObject.Find("OrderDropdown").GetComponent<Dropdown>();
        PageInput = GameObject.Find("PageInput").GetComponent<InputField>();
        HasKnownLocationToggle = GameObject.Find("HasKnownLocationToggle").GetComponent<Toggle>();
        SearchButton = GameObject.Find("SearchButton").GetComponent<Button>();
        SearchButton.onClick.AddListener(OnButtonSearch);
        CloseButton = GameObject.Find("CloseButton").GetComponent<Button>();
        CloseButton.onClick.AddListener(() =>
        {
            CloseSearchWindow();
        });

        ResultsContent = GameObject.Find("ResultsContent").GetComponent<RectTransform>();
        resultsContentOriginalHeight = ResultsContent.sizeDelta.y;
        NextPageButton = GameObject.Find("NextPageButton").GetComponent<Button>();
        NextPageButton.onClick.AddListener(OnNextPageButton);
        PreviousPageButton = GameObject.Find("PreviousPageButton").GetComponent<Button>();
        PreviousPageButton.onClick.AddListener(OnPreviousPageButton);

        SearchResultLayout = GameObject.Find("SearchResultLayout");
        ThumbnailImage = GameObject.Find("ThumbnailImage").GetComponent<RawImage>();
        NameText = GameObject.Find("NameText").GetComponent<Text>();
        DescriptionText = GameObject.Find("DescriptionText").GetComponent<Text>();
        LoadButton = GameObject.Find("LoadButton").GetComponent<Button>();
        LocationText = GameObject.Find("LocationText").GetComponent<Text>();
        SearchResultLayout.SetActive(false);

        SearchCanvas.SetActive(false);
    }
    public void ResetPage()
    {
        page = 0;
    }

    private void OnPreviousPageButton()
    {
        if (page > 1)
        {
            page -= 1;
            PageInput.text = page.ToString();
            DoSearch(page);
        }        
    }
    private void OnNextPageButton()
    {
        page += 1;
        PageInput.text = page.ToString();
        DoSearch(page);
    }
    private void OnButtonSearch()
    {
        //Consider using a cooroutine
        page = int.Parse(PageInput.text);
        DoSearch(page);
    }

    private void DoSearch(int Page)
    {
        loadingObject.gameObject.SetActive(true);
        ClearResults();
        StartCoroutine(VRCAPIHandler.GetAvatarsList(OnSearchResponse, (Page * 10), SearchInput.text, OrderDropdown.options[OrderDropdown.value].text, SortDropdown.options[SortDropdown.value].text, OnSearchError));
    }

    private void OnSearchError(string obj)
    {
        Debug.Log("Search error: " + obj);
    }

    private void ClearResults()
    {
        for (int i = 0; i < avatarSearchGameobjects.Count; i++)
        {
            Destroy(avatarSearchGameobjects[i]);
        }
        avatarSearchGameobjects.Clear();
    }

    private void OnSearchResponse(List<VRCAPIHandler.AvatarListItem> obj)
    {
        loadingObject.gameObject.SetActive(false);

        ClearResults();

        ResultsContent.sizeDelta = new Vector2(ResultsContent.sizeDelta.x, resultsContentOriginalHeight + (SearchResultLayout.GetComponent<RectTransform>().sizeDelta.y * obj.Count));
        Debug.Log("Adding " + obj.Count + " items");
        for (int i = 0; i < obj.Count; i++)
        {
            var newResultGameobject = Instantiate(SearchResultLayout, ResultsContent);
            avatarSearchGameobjects.Add(newResultGameobject);
            newResultGameobject.transform.parent = ResultsContent.transform;
            newResultGameobject.SetActive(true);
            var rect = newResultGameobject.GetComponent<RectTransform>();
            rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, SearchResultLayout.GetComponent<RectTransform>().anchoredPosition.y - (i * rect.sizeDelta.y));
            if (DownloadImages.isOn)
                StartCoroutine(SetThumbnailImage(obj[i].imageUrl, newResultGameobject.transform.Find("ThumbnailImage").GetComponent<RawImage>()));
            newResultGameobject.transform.Find("NameText").GetComponent<Text>().text = obj[i].name + " by " + obj[i].authorName;
            newResultGameobject.transform.Find("DescriptionText").GetComponent<Text>().text = obj[i].description;
            var locationText = newResultGameobject.transform.Find("LocationText").GetComponent<Text>();
            locationText.text = "Getting location...";
            StartCoroutine(SetLocation(obj[i].id, locationText));
            string avatarId = obj[i].id;
            newResultGameobject.transform.Find("LoadButton").GetComponent<Button>().onClick.AddListener(() =>
            {
                Debug.Log("Load button, load avatar: " + avatarId);
                CloseSearchWindow();
                AvatarLoader.instance.LoadAvatar(avatarId);
            });
        }
    }

    private IEnumerator SetThumbnailImage(string url, RawImage image)
    {
        if (image != null)
        {
            url = VRCAPIHandler.CheckProxyUrl(url, true);
            Texture2D tex;
            tex = new Texture2D(4, 4, TextureFormat.DXT1, false);
            Debug.Log("Getting image from " + url);
            using (WWW www = new WWW(url))
            {
                yield return www;
                Debug.Log("Got image response. Size: " + Mathf.Round(www.bytes.Length / 1000).ToString() + " KB");
                if (image != null)
                {
                    www.LoadImageIntoTexture(tex);
                    image.texture = tex;
                }
                //GetComponent<Renderer>().material.mainTexture = tex;
            }
        }
    }
    private IEnumerator SetLocation(string id, Text text)
    {
        if (text != null)
        {
            yield return null;
            text.text = "Unkown";
        }
    }
    private IEnumerator GetSearchResulst()
    {
        yield return null;
        //VRCAPIHandler.GetAvatarsList(OnSearchResponse, int.Parse(PageInput), SearchInput.text, OrderDropdown.value, SortDropdown.value, OnSearchError);
    }

    private void CloseSearchWindow(bool toggle = false)
    {
        if (toggle)
            SearchCanvas.SetActive(!SearchCanvas.activeSelf);
        else
            SearchCanvas.SetActive(false);

        page = 0;
        IsActive = SearchCanvas.activeSelf;
    }

    // Update is called once per frame
    void Update ()
    {
		
	}

    
}
