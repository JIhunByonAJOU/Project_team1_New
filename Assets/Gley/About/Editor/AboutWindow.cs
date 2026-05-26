namespace Gley.About.Editor
{
    using Gley.Common.Editor;
    using System.Linq;
    using UnityEditor;
    using UnityEngine;

    public class AboutWindow : EditorWindow
    {
        struct ContactButton
        {
            public GUIContent GuiContent;
            public string Url;

            public ContactButton(GUIContent guiContent, string url)
            {
                GuiContent = guiContent;
                Url = url;
            }
        }

        private static AssetStorePackage[] assetStorePackages;
        private static IconReferences iconReferences;
        private static string rootFolder;

        private ContactButton[] contactButtons;
        private Vector2 scrollPosition;

        [MenuItem(AboutWindowProperties.menuItem, false, 0)]
        private static void Init()
        {
            WindowLoader.LoadWindow<AboutWindow>(new AboutWindowProperties(), new Version(), out rootFolder);
        }

        private static void LoadAssetStorePackages()
        {
            assetStorePackages = new AssetStorePackage[]
            {
                new AssetStorePackage("UrbanExample", "Urban Traffic & Pedestrian System", iconReferences.urbanIcon, "Bring your city environment to life with realistic vehicle and pedestrian systems.",
                "https://assetstore.unity.com/packages/slug/285687?aid=1011l8QY4"),
                new AssetStorePackage("TrafficSystem", "Traffic System", iconReferences.trafficSystemIcon, "Highly performant and easy to use traffic system that can make any driving game more fun to play in just a few clicks.",
                "https://assetstore.unity.com/packages/slug/305800?aid=1011l8QY4"),
                 new AssetStorePackage("PedestrianSystem", "Pedestrian System", iconReferences.pedestrianSystemIcon, "Create a realistic and vibrant urban environment with a customizable and dynamic Pedestrian System.",
                "https://assetstore.unity.com/packages/slug/203706?aid=1011l8QY4"),
                 new AssetStorePackage("CakeRush", "Cake Rush", iconReferences.cakeRushIcon, "A complete arcade casual game, ready to be published or integrated as a mini-game within a larger project.",
                "https://assetstore.unity.com/packages/slug/203708?aid=1011l8QY4"),
                 new AssetStorePackage("MobileTrafficTruck", "Traffic Truck", iconReferences.trafficTruckIcon, "Highly optimized, low-poly PBR pack containing a truck head, a tanker, a trailer, and a hangar garage",
                "https://assetstore.unity.com/packages/slug/273684?aid=1011l8QY4"),
                 new AssetStorePackage("DeliveryVehiclesPack", "Delivery Vehicles Pack", iconReferences.vehiclesIcon, "Delivery Vehicles Pack contains 3 low poly, textured vehicles: Scooter, Three Wheeler, Minivan",
                "https://assetstore.unity.com/packages/3d/vehicles/land/delivery-vehicles-pack-55528?aid=1011l8QY4")
                //new AssetStorePackage("Jumpy", "Mobile Tools", iconReferences.mobileToolsIcon, "All you need to publish your finished game on the store and BONUS a free game with all of them already integrated",
                //"https://assetstore.unity.com/packages/slug/266719?aid=1011l8QY4"),
                //new AssetStorePackage("Ads", "Mobile Ads", iconReferences.mobileAdsIcon, "Show ads inside your game with this easy to use, multiple advertisers support tool.",
                //"https://assetstore.unity.com/packages/slug/266331?aid=1011l8QY4"),
                //new AssetStorePackage("EasyIAP", "Easy IAP", iconReferences.easyIAPIcon, "Sell In App products inside your game with minimal setup and very little programming knowledge.",
                //"https://assetstore.unity.com/packages/slug/264594?aid=1011l8QY4"),
                //new AssetStorePackage("Localization", "Localization (Multi-Language)", iconReferences.localizationIcon, "Make your app international and reach a greater audience by translating your app in multiple languages.",
                //"https://assetstore.unity.com/packages/slug/264640?aid=1011l8QY4"),
                //new AssetStorePackage("DailyRewards", "Daily (Time Based) Rewards", iconReferences.dailyRewardsIcon, "Increase the retention of your game by using Daily Rewards and Time Based rewards.",
                //"https://assetstore.unity.com/packages/slug/264442?aid=1011l8QY4"),
                //new AssetStorePackage("Notifications", "Mobile Push Notifications",iconReferences.notificationsIcon, "Send scheduled offline notifications to your users and keep them engaged.",
                //"https://assetstore.unity.com/packages/slug/264705?aid=1011l8QY4"),
                //new AssetStorePackage("GameServices", "Easy Achievements and Leaderboards", iconReferences.achievementsIcon, "Submit achievements and scores with minimal setup and increase competition between your users.",
                //"https://assetstore.unity.com/packages/slug/264568?aid=1011l8QY4"),
                //new AssetStorePackage("RateGame", "Rate Game Popup", iconReferences.rateGameIcon, "Increase the number of game ratings by encouraging users to rate your game.",
                //"https://assetstore.unity.com/packages/slug/264661?aid=1011l8QY4"),
                //new AssetStorePackage("CrossPromo", "Mobile Cross Promo", iconReferences.crossPromoIcon, "Easily cross promote and increase popularity for all of your published games.",
                //"https://assetstore.unity.com/packages/slug/264649?aid=1011l8QY4"),
                //new AssetStorePackage("AllPlatformsSave", "All Platforms Save", iconReferences.saveIcon, "Easy to use: same line of code to save or load game data on all supported Unity platforms.",
                //"https://assetstore.unity.com/packages/slug/264406?aid=1011l8QY4"),

            };
        }

        private static void LoadIcons()
        {
            iconReferences = AssetDatabase.FindAssets("t:IconReferences").Select(guid => AssetDatabase.LoadAssetAtPath<IconReferences>(AssetDatabase.GUIDToAssetPath(guid))).FirstOrDefault();
        }

        private void OnEnable()
        {
            if (rootFolder == null)
            {
                rootFolder = WindowLoader.GetRootFolder(new AboutWindowProperties());
            }

            if (iconReferences == null)
            {
                LoadIcons();
            }

            if (assetStorePackages == null)
            {
                LoadAssetStorePackages();
            }
            contactButtons = new ContactButton[]
            {
                new ContactButton(new GUIContent(" Website", iconReferences.websiteIcon),"https://gleygames.com"),
                new ContactButton(new GUIContent(" Youtube", iconReferences.youtubeIcon),"https://www.youtube.com/c/gleygames"),
                new ContactButton(new GUIContent(" Discord", iconReferences.discordIcon),"https://discord.gg/7eSvKKW"),
                new ContactButton(new GUIContent(" Twitter", iconReferences.twitterIcon),"https://twitter.com/GleyGames"),
                //new ContactButton(new GUIContent(" Facebook", iconReferences.facebookIcon),"https://www.youtube.com/c/gleygames"),
                //new ContactButton(new GUIContent(" Instagram", iconReferences.instagramIcon),"https://www.instagram.com/gleygames/")
            };
        }

        private void OnGUI()
        {
            GUIStyle labelStyle = new GUIStyle(GUI.skin.label);
            labelStyle.alignment = TextAnchor.UpperCenter;
            EditorGUILayout.Space();
            GUILayout.Label(iconReferences.gleyCover, labelStyle);
            labelStyle.fontStyle = FontStyle.Bold;
            GUILayout.Label("Professional assets made easy to use for everyone", labelStyle);
            EditorGUILayout.Space();

            GUILayout.Label("Connect with us:", labelStyle);
            EditorGUILayout.SelectableLabel("gley.assets@gmail.com", labelStyle);

            EditorGUILayout.BeginHorizontal();
            for (int i = 0; i < contactButtons.Length; i++)
            {
                if (GUILayout.Button(contactButtons[i].GuiContent))
                {
                    Application.OpenURL(contactButtons[i].Url);
                }
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space();

            if (GUILayout.Button("Open Asset Store Publisher Page"))
            {
                Application.OpenURL("https://assetstore.unity.com/publishers/19336");
            }
            EditorGUILayout.Space();

            scrollPosition = GUILayout.BeginScrollView(scrollPosition, false, false, GUILayout.Width(position.width), GUILayout.Height(position.height - 250));

            DrawPackages();

            GUILayout.EndScrollView();
        }

        private void DrawPackages()
        {
            for (int i = 0; i < assetStorePackages.Length; i++)
            {
                DrawPack(assetStorePackages[i]);
            }
        }

        private void DrawPack(AssetStorePackage pack)
        {
            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            EditorGUILayout.Space();
            GUIStyle style = new GUIStyle(GUI.skin.label);
            style.fontSize = 18;
            style.alignment = TextAnchor.MiddleLeft;
            GUILayout.Label(pack.texture, style);
            EditorGUILayout.Space();
            EditorGUILayout.BeginVertical();
            GUILayout.Label(pack.name, style);
            style.fontSize = 12;
            style.wordWrap = true;
            //style.normal.background = downloadColor;
            GUILayout.Label(pack.description, style);
            EditorGUILayout.EndVertical();
            var oldColor = GUI.backgroundColor;
            string buttonText = "";
            switch (pack.assetState)
            {
                case AssetState.ComingSoon:
                    GUI.backgroundColor = new Color32(190, 190, 190, 255);
                    buttonText = "Coming Soon";
                    break;
                case AssetState.InProject:
                    GUI.backgroundColor = new Color32(253, 195, 71, 255);
                    buttonText = "Owned";
                    break;
                case AssetState.NotDownloaded:
                    GUI.backgroundColor = new Color32(42, 180, 240, 255);
                    buttonText = "Download";
                    break;
                case AssetState.UpdateAvailable:
                    GUI.backgroundColor = new Color32(76, 229, 89, 255);
                    buttonText = "Update Available";
                    break;
            }

            if (GUILayout.Button(buttonText, GUILayout.Width(130), GUILayout.Height(64)))
            {
                Application.OpenURL(pack.url);
            }

            GUI.backgroundColor = oldColor;
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space();
        }
    }
}
