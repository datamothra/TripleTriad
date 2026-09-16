using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// Menu: Triad → Build Scene. Creates the song asset, the Player prefab, and a scene with everything wired.
public static class TriadSetup
{
    static readonly Color Accent = new Color(0.875f, 0.686f, 0.196f);
    static readonly Color Ink    = new Color(0.070f, 0.075f, 0.106f);

    [MenuItem("Triad/Build Scene")]
    public static void BuildScene()
    {
        AssetDatabase.Refresh();                                   // pick up files dropped in while the editor was unfocused
        if (!AssetDatabase.IsValidFolder("Assets/TextMesh Pro"))  // TMP essentials, without the dialog
        {
            const string tmp = "Packages/com.unity.ugui/Package Resources/TMP Essential Resources.unitypackage";
#pragma warning disable 618
            if (System.IO.File.Exists(System.IO.Path.GetFullPath(tmp))) { AssetDatabase.ImportPackage(tmp, false); AssetDatabase.Refresh(); }
#pragma warning restore 618
        }
        var circle = ImportSprite("Assets/Sprites/circle.png", 512);
        var ring = ImportSprite("Assets/Sprites/ring.png", 512);
        var actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/Input/TriadControls.inputactions");
        if (circle == null || ring == null || actions == null)
        {
            Debug.LogError("Triad: missing circle.png, ring.png or TriadControls.inputactions under Assets.");
            return;
        }

        var spriteMat = SpriteMaterial();
        var songs = MakeSongs();
        var playerPrefab = MakePlayerPrefab(circle, actions);

        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        var cam = Camera.main;
        cam.orthographic = true;
        cam.orthographicSize = 5.6f;
        cam.transform.position = new Vector3(0, 0, -10);
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.05f, 0.05f, 0.08f);

        // the ring and the red deadline
        var ringMesh = new GameObject("Ring").AddComponent<RingMesh>();
        var coreLine = MakeSprite("CoreLine", circle, Vector3.zero, 3.5f, new Color(0.784f, 0.294f, 0.294f), 5);
        MakeSprite("Core", circle, Vector3.zero, 3.35f, Ink, 6);
        var letters = new GameObject("Letters");
        for (int i = 0; i < 12; i++)
            MakeText("Letter " + Chord.Names[i], GameManager.Polar(3.4f, i), Chord.Names[i], 8, new Color(1, 1, 1, 0.08f), 3, true).transform.SetParent(letters.transform, true);

        // the game manager and its feedback objects
        var manager = new GameObject("GameManager").AddComponent<GameManager>();
        var approach = MakeSprite("ApproachCircle", ring, Vector3.zero, 24f, Accent, 18);
        approach.gameObject.SetActive(false);
        var label = MakeText("ChordLabel", new Vector3(0, 0.22f, 0), "", 8, Color.white, 50, true);
        var notes = MakeText("ChordNotes", new Vector3(0, -0.35f, 0), "", 3.4f, new Color(1, 1, 1, 0.6f), 50);
        manager.songs = songs;
        manager.ring = ringMesh;
        manager.coreLine = coreLine.transform;
        manager.chordNotes = notes;
        manager.ringTemplate = approach.transform;
        manager.chordLabel = label;
        manager.accent = Accent;

        var tri = new GameObject("ChordTriangle");
        var line = tri.AddComponent<LineRenderer>();
        line.positionCount = 3;
        line.loop = true;
        line.startWidth = line.endWidth = 0.08f;
        line.useWorldSpace = true;
        line.sharedMaterial = spriteMat;
        line.sortingOrder = 9;
        var triangle = tri.AddComponent<ChordTriangle>();
        triangle.lit = Accent;
        triangle.game = manager;
        manager.triangle = triangle;

        manager.synth = new GameObject("Synth").AddComponent<Triad.Synth>();      // the only sound source: synthesized piano strikes

        // one player per controller
        var inputManager = new GameObject("PlayerManager").AddComponent<PlayerInputManager>();
        inputManager.playerPrefab = playerPrefab;
        inputManager.joinBehavior = PlayerJoinBehavior.JoinPlayersWhenButtonIsPressed;
        inputManager.notificationBehavior = PlayerNotifications.InvokeCSharpEvents;   // so GameManager gets onPlayerJoined
        manager.playerManager = inputManager;
        var managerSo = new SerializedObject(inputManager);       // maxPlayerCount is read-only at runtime, so set the field
        var maxProp = managerSo.FindProperty("m_MaxPlayerCount");
        if (maxProp != null) { maxProp.intValue = 3; managerSo.ApplyModifiedPropertiesWithoutUndo(); }
        else Debug.LogWarning("Triad: could not set Max Player Count; set it to 3 on PlayerManager by hand.");

        // speed slider, wired to the game manager as a persistent UnityEvent listener, like dragging it in the inspector
        var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);

        var sliderGo = DefaultControls.CreateSlider(new DefaultControls.Resources());
        sliderGo.name = "SpeedSlider";
        sliderGo.transform.SetParent(canvasGo.transform, false);
        var srt = sliderGo.GetComponent<RectTransform>();
        srt.anchorMin = srt.anchorMax = srt.pivot = new Vector2(1, 0);
        srt.anchoredPosition = new Vector2(-24, 24);
        srt.sizeDelta = new Vector2(220, 20);
        var slider = sliderGo.GetComponent<Slider>();
        slider.minValue = 0.4f; slider.maxValue = 2.5f; slider.value = 1f;
        Tint(sliderGo, "Background", new Color(1, 1, 1, 0.15f));
        Tint(sliderGo, "Fill Area/Fill", Accent);
        Tint(sliderGo, "Handle Slide Area/Handle", Color.white);
        UnityEventTools.AddPersistentListener(slider.onValueChanged, manager.SetSpeed);

        manager.speedLabel = HudText(canvasGo.transform, "SpeedLabel", "SPEED 1.0x", 14, TextAlignmentOptions.Right, new Vector2(1, 0), new Vector2(-24, 50), new Vector2(220, 20));
        manager.titleLabel = HudText(canvasGo.transform, "Title", "", 16, TextAlignmentOptions.Center, new Vector2(0.5f, 1), new Vector2(0, -16), new Vector2(900, 24));
        manager.scoreLabel = HudText(canvasGo.transform, "Score", "", 22, TextAlignmentOptions.TopRight, new Vector2(1, 1), new Vector2(-24, -16), new Vector2(300, 120));
        manager.bannerLabel = HudText(canvasGo.transform, "Banner", "", 26, TextAlignmentOptions.Center, new Vector2(0.5f, 0), new Vector2(0, 22), new Vector2(1100, 34));
        manager.bannerLabel.fontStyle = FontStyles.Bold;
        manager.scoreLabel.fontStyle = FontStyles.Bold;

        new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));

        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Triad.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Triad.unity", true) };
        Debug.Log("Triad: scene built and saved as Assets/Scenes/Triad.unity. Press Play, then Cross on each of the three controllers to join.");
    }

    // ---- pieces -----------------------------------------------------------------------------

    static Song MakeSong(string file, string title, string meter, float bpm, float beatsPerChord, string chart)
    {
        var song = ScriptableObject.CreateInstance<Song>();
        song.meter = meter;
        song.bpm = bpm;
        foreach (var sym in chart.Split(' '))
        {
            if (sym.Length == 0) continue;
            bool minor = sym.EndsWith("m");
            string rootName = minor ? sym.Substring(0, sym.Length - 1) : sym;
            var root = (Chord.Note)System.Enum.Parse(typeof(Chord.Note), rootName.Replace("#", "s"));
            song.entries.Add(new Song.Entry { root = root, quality = minor ? Chord.Quality.Minor : Chord.Quality.Major, beats = beatsPerChord });
        }
        Folder("Assets/Songs");
        AssetDatabase.CreateAsset(song, "Assets/Songs/" + file + ".asset");
        song.name = title;
        return song;
    }

    /// Three public-domain charts, each in its own meter. One chord symbol per bar of the chart
    /// unless the harmonic rhythm is faster (the Canon changes every half note).
    static Song[] MakeSongs() => new[]
    {
        // 6/8 counted in three: each bar is three pulses (117 of them a minute keeps the bar at 78 dotted quarters).
        MakeSong("HouseOfTheRisingSun", "House of the Rising Sun", "6/8 in 3", 72, 3,
                 "Am C D F  Am C E E  Am C D F  Am E Am E"),
        // 4/4, one chord per bar.
        MakeSong("PachelbelCanon", "Pachelbel's Canon in D", "4/4", 84, 4,
                 "D A Bm F#m G D G A"),
        // 3/4, one chord per bar, sixteen bars in G.
        MakeSong("AmazingGrace", "Amazing Grace", "3/4", 66, 3,
                 "G G C G  G Em D D  G G C G  Em D G G"),
    };

    static GameObject MakePlayerPrefab(Sprite circle, InputActionAsset actions)
    {
        var root = new GameObject("Player");
        MakeSprite("Pad", circle, Vector3.zero, 0.7f, new Color(0.92f, 0.92f, 0.95f), 10).transform.SetParent(root.transform, false);
        var label = MakeText("NoteLabel", Vector3.zero, "C", 4, Ink, 12, true);
        label.transform.SetParent(root.transform, false);
        var tag = MakeText("Tag", new Vector3(0, -0.58f, 0), "P1", 2.2f, new Color(0.92f, 0.92f, 0.95f, 0.9f), 12);
        tag.transform.SetParent(root.transform, false);

        var input = root.AddComponent<PlayerInput>();
        input.actions = actions;
        input.defaultActionMap = "Player";
        input.notificationBehavior = PlayerNotifications.SendMessages;

        var voice = root.AddComponent<PlayerVoice>();
        voice.noteLabel = label;
        voice.tagLabel = tag;

        Folder("Assets/Prefabs");
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, "Assets/Prefabs/Player.prefab");
        Object.DestroyImmediate(root);
        return prefab;
    }

    static Material SpriteMaterial()
    {
        Folder("Assets/Materials");
        const string path = "Assets/Materials/TriadSprite.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat != null) return mat;
        var shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default") ?? Shader.Find("Sprites/Default");
        mat = new Material(shader);
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    static Sprite ImportSprite(string path, int pixelsPerUnit)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) return null;
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = pixelsPerUnit;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    static SpriteRenderer MakeSprite(string name, Sprite sprite, Vector3 pos, float scale, Color color, int order)
    {
        var go = new GameObject(name);
        go.transform.position = pos;
        go.transform.localScale = Vector3.one * scale;
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.color = color;
        sr.sortingOrder = order;
        return sr;
    }

    static TextMeshPro MakeText(string name, Vector3 pos, string text, float size, Color color, int order, bool bold = false)
    {
        var go = new GameObject(name);
        go.transform.position = pos;
        var tmp = go.AddComponent<TextMeshPro>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.rectTransform.sizeDelta = new Vector2(6, 2);
        tmp.sortingOrder = order;
        if (bold) tmp.fontStyle = FontStyles.Bold;
        return tmp;
    }

    static TextMeshProUGUI HudText(Transform canvas, string name, string text, float size, TextAlignmentOptions align, Vector2 anchor, Vector2 pos, Vector2 sizeDelta)
    {
        var t = new GameObject(name, typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
        t.transform.SetParent(canvas, false);
        t.text = text;
        t.fontSize = size;
        t.alignment = align;
        t.color = new Color(1, 1, 1, 0.85f);
        var rt = t.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
        rt.anchoredPosition = pos;
        rt.sizeDelta = sizeDelta;
        return t;
    }

    static void Tint(GameObject sliderGo, string path, Color c)
    {
        var t = sliderGo.transform.Find(path);
        if (t != null && t.TryGetComponent<Image>(out var img)) img.color = c;
    }

    static void Folder(string path)
    {
        if (!AssetDatabase.IsValidFolder(path))
            AssetDatabase.CreateFolder(System.IO.Path.GetDirectoryName(path).Replace('\\', '/'), System.IO.Path.GetFileName(path));
    }
}
