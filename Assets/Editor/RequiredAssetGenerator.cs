using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using ZWebGLFrameWork.Combat;
using ZWebGLFrameWork.Data;
using ZWebGLFrameWork.UI;

namespace ZWebGLFrameWork.Editor
{
    public static class RequiredAssetGenerator
    {
        private const string ViewConfigPath =
            "Assets/Resources/Config/ViewConfig.asset";

        private static readonly string[] Modes =
        {
            "builder_base", "campaign", "clan_war", "village_attack"
        };

        private static readonly (int id, string name, string path, PrimitiveType primitive)[] Archetypes =
        {
            (1, "TownHall", "Assets/Resources/Prefabs/Buildings/TownHall.prefab", PrimitiveType.Cube),
            (2, "Cannon", "Assets/Resources/Prefabs/Buildings/Cannon.prefab", PrimitiveType.Cylinder),
            (3, "ArcherTower", "Assets/Resources/Prefabs/Buildings/ArcherTower.prefab", PrimitiveType.Cylinder),
            (10, "Barbarian", "Assets/Resources/Prefabs/Units/Barbarian.prefab", PrimitiveType.Capsule),
            (11, "Archer", "Assets/Resources/Prefabs/Units/Archer.prefab", PrimitiveType.Capsule),
        };

        private static readonly Type[] PanelTypes =
        {
            typeof(BattleHUD), typeof(BuildingInfoPanel), typeof(CharacterSelectPanel),
            typeof(DeployPanel), typeof(ErrorPanel), typeof(ForceUpdatePanel),
            typeof(LoadingPanel), typeof(LoginPanel), typeof(MainVillageHUD),
            typeof(ResultPanel), typeof(ToastPanel),
        };

        [MenuItem("ZWebGL/Generate Required Runtime Assets", false, 90)]
        public static void Generate()
        {
            UnitConfig[] units = ConfigLoader.LoadJsonArrayFromResources<UnitConfig>("Config/Units")
                .OrderBy(unit => unit.Id)
                .ToArray();
            if (units.Length == 0)
                throw new InvalidOperationException("Units.json must contain at least one unit.");
            if (units.Select(unit => unit.Id).Distinct().Count() != units.Length)
                throw new InvalidOperationException("Units.json contains duplicate unit ids.");

            Directory.CreateDirectory("Assets/Resources/Prefabs/Units");
            Directory.CreateDirectory("Assets/Resources/Prefabs/Buildings");
            Directory.CreateDirectory("Assets/Resources/UI/Panels");

            foreach (UnitConfig unit in units)
            {
                CreatePrimitivePrefab(
                    $"Assets/Resources/Prefabs/Units/{unit.Name}.prefab",
                    unit.Name,
                    PrimitiveType.Capsule);
            }

            foreach (var archetype in Archetypes.Where(value => value.id < 10))
                CreatePrimitivePrefab(archetype.path, archetype.name, archetype.primitive);

            foreach (Type panelType in PanelTypes.OrderBy(type => type.Name, StringComparer.Ordinal))
                CreatePanelPrefab(panelType);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            CreateViewConfig();
            WriteManifest(units);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            RequiredAssetValidationResult validation = RequiredAssetManifestValidator.Validate(
                RequiredAssetManifestValidator.ManifestPath);
            if (!validation.IsValid)
                throw new InvalidOperationException(string.Join("\n", validation.Errors));
        }

        private static void CreatePrimitivePrefab(
            string path, string name, PrimitiveType primitiveType)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null) return;

            GameObject instance = GameObject.CreatePrimitive(primitiveType);
            try
            {
                instance.name = name;
                PrefabUtility.SaveAsPrefabAsset(instance, path);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private static void CreatePanelPrefab(Type panelType)
        {
            string path = $"Assets/Resources/UI/Panels/{panelType.Name}.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null) return;

            var instance = new GameObject(panelType.Name, typeof(RectTransform));
            try
            {
                PrefabUtility.SaveAsPrefabAsset(instance, path);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private static void CreateViewConfig()
        {
            ViewConfig config = AssetDatabase.LoadAssetAtPath<ViewConfig>(ViewConfigPath);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<ViewConfig>();
                AssetDatabase.CreateAsset(config, ViewConfigPath);
            }

            config.Entries = Archetypes
                .OrderBy(value => value.id)
                .Select(value => new ViewConfig.ArchetypeEntry
                {
                    ArchetypeId = value.id,
                    Prefab = AssetDatabase.LoadAssetAtPath<GameObject>(value.path),
                })
                .ToArray();
            EditorUtility.SetDirty(config);
        }

        private static void WriteManifest(UnitConfig[] units)
        {
            var entries = new List<RequiredAssetEntry>
            {
                Entry("config.app", "Assets/Resources/Config/AppConfig.asset", "ScriptableObject"),
                Entry("config.buildings", "Assets/Resources/Config/Buildings.json", "TextAsset"),
                Entry("config.levels", "Assets/Resources/Config/Levels.json", "TextAsset"),
                Entry("config.units", "Assets/Resources/Config/Units.json", "TextAsset"),
                Entry("config.views", ViewConfigPath, "ScriptableObject"),
                Entry("scene.launcher", "Assets/Scence/Launcher.unity", "SceneAsset"),
            };

            entries.AddRange(units.Select(unit => Entry(
                $"unit.{unit.Id:D4}",
                $"Assets/Resources/Prefabs/Units/{unit.Name}.prefab",
                "GameObject")));
            entries.AddRange(Archetypes.Where(value => value.id < 10).Select(value => Entry(
                $"archetype.{value.id:D4}", value.path, "GameObject")));
            entries.AddRange(PanelTypes.Select(type => Entry(
                $"ui.{type.Name}",
                $"Assets/Resources/UI/Panels/{type.Name}.prefab",
                "GameObject")));

            var document = new RequiredAssetManifestDocument
            {
                version = 1,
                supportedModes = Modes.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
                archetypeIds = Archetypes.Select(value => value.id)
                    .OrderBy(value => value).ToArray(),
                unitIds = units.Select(unit => unit.Id).OrderBy(value => value).ToArray(),
                uiPanels = PanelTypes.Select(type => type.Name)
                    .OrderBy(value => value, StringComparer.Ordinal).ToArray(),
                entries = entries.OrderBy(entry => entry.key, StringComparer.Ordinal).ToArray(),
            };

            File.WriteAllText(
                RequiredAssetManifestValidator.ManifestPath,
                JsonUtility.ToJson(document, true) + "\n",
                new UTF8Encoding(false));
        }

        private static RequiredAssetEntry Entry(string key, string path, string expectedType)
        {
            return new RequiredAssetEntry
            {
                key = key,
                assetPath = path,
                expectedType = expectedType,
            };
        }
    }
}
