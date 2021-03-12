﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using Common;
using EpicLoot.Crafting;
using ExtendedItemDataFramework;
using fastJSON;
using HarmonyLib;
using UnityEngine;
using Random = UnityEngine.Random;

namespace EpicLoot
{
    public class Assets
    {
        public Sprite EquippedSprite;
        public Sprite GenericSetItemSprite;
        public Sprite GenericItemBgSprite;
        public GameObject[] MagicItemLootBeamPrefabs = new GameObject[4];
        public readonly Dictionary<string, GameObject[]> CraftingMaterialPrefabs = new Dictionary<string, GameObject[]>();
    }

    public class PieceDef
    {
        public string Table;
        public string CraftingStation;
        public string ExtendStation;
        public List<RecipeRequirementConfig> Resources = new List<RecipeRequirementConfig>();
    }

    [BepInPlugin(PluginId, "Epic Loot", Version)]
    [BepInDependency("randyknapp.mods.extendeditemdataframework")]
    public class EpicLoot : BaseUnityPlugin
    {
        private const string PluginId = "randyknapp.mods.epicloot";
        private const string Version = "0.5.5";

        private static ConfigEntry<string> SetItemColor;
        private static ConfigEntry<string> MagicRarityColor;
        private static ConfigEntry<string> RareRarityColor;
        private static ConfigEntry<string> EpicRarityColor;
        private static ConfigEntry<string> LegendaryRarityColor;
        private static ConfigEntry<int> MagicMaterialIconColor;
        private static ConfigEntry<int> RareMaterialIconColor;
        private static ConfigEntry<int> EpicMaterialIconColor;
        private static ConfigEntry<int> LegendaryMaterialIconColor;

        public static readonly List<ItemDrop.ItemData.ItemType> AllowedMagicItemTypes = new List<ItemDrop.ItemData.ItemType>
        {
            ItemDrop.ItemData.ItemType.Helmet,
            ItemDrop.ItemData.ItemType.Chest,
            ItemDrop.ItemData.ItemType.Legs,
            ItemDrop.ItemData.ItemType.Shoulder,
            ItemDrop.ItemData.ItemType.Utility,
            ItemDrop.ItemData.ItemType.Bow,
            ItemDrop.ItemData.ItemType.OneHandedWeapon,
            ItemDrop.ItemData.ItemType.TwoHandedWeapon,
            ItemDrop.ItemData.ItemType.Shield,
            ItemDrop.ItemData.ItemType.Tool,
            ItemDrop.ItemData.ItemType.Torch,
        };

        public static readonly Dictionary<string, string> MagicItemColors = new Dictionary<string, string>()
        {
            { "Red",    "#ff4545" },
            { "Orange", "#ffac59" },
            { "Yellow", "#ffff75" },
            { "Green",  "#80fa70" },
            { "Teal",   "#18e7a9" },
            { "Blue",   "#00abff" },
            { "Indigo", "#709bba" },
            { "Purple", "#d078ff" },
            { "Pink",   "#ff63d6" },
            { "Gray",   "#dbcadb" },
        };

        public static readonly List<string> RestrictedItemNames = new List<string>
        {
            "$item_tankard", "$item_tankard_odin", "Unarmed", "CAPE TEST"
        };

        public static Dictionary<ItemRarity, Dictionary<int, float>> MagicEffectCountWeightsPerRarity = new Dictionary<ItemRarity, Dictionary<int, float>>()
        {
            { ItemRarity.Magic, new Dictionary<int, float>() { { 1, 80 }, { 2, 18 }, { 3, 2 } } },
            { ItemRarity.Rare, new Dictionary<int, float>() { { 2, 80 }, { 3, 18 }, { 4, 2 } } },
            { ItemRarity.Epic, new Dictionary<int, float>() { { 3, 80 }, { 4, 18 }, { 5, 2 } } },
            { ItemRarity.Legendary, new Dictionary<int, float>() { { 4, 80 }, { 5, 18 }, { 6, 2 } } }
        };

        public static readonly Assets Assets = new Assets();
        public static readonly Dictionary<string, List<LootTable>> LootTables = new Dictionary<string, List<LootTable>>();
        public static readonly List<GameObject> RegisteredPrefabs = new List<GameObject>();
        public static readonly List<GameObject> RegisteredItemPrefabs = new List<GameObject>();
        public static readonly Dictionary<GameObject, PieceDef> RegisteredPieces = new Dictionary<GameObject, PieceDef>();

        public static event Action LootTableLoaded;
        public static event Action<ExtendedItemData, MagicItem> MagicItemGenerated;

        private Harmony _harmony;

        private static WeightedRandomCollection<int[]> _weightedDropCountTable;
        private static WeightedRandomCollection<LootDrop> _weightedLootTable;
        private static WeightedRandomCollection<MagicItemEffectDefinition> _weightedEffectTable;
        private static WeightedRandomCollection<KeyValuePair<int, float>> _weightedEffectCountTable;
        private static WeightedRandomCollection<KeyValuePair<ItemRarity, int>> _weightedRarityTable;

        private void Awake()
        {
            var random = new System.Random();
            _weightedDropCountTable = new WeightedRandomCollection<int[]>(random);
            _weightedLootTable = new WeightedRandomCollection<LootDrop>(random);
            _weightedEffectTable = new WeightedRandomCollection<MagicItemEffectDefinition>(random);
            _weightedEffectCountTable = new WeightedRandomCollection<KeyValuePair<int, float>>(random);
            _weightedRarityTable = new WeightedRandomCollection<KeyValuePair<ItemRarity, int>>(random);

            MagicRarityColor = Config.Bind("Item Colors", "Magic Rarity Color", "Blue", "The color of Magic rarity items, the lowest magic item tier. (Optional, use an HTML hex color starting with # to have a custom color.) Available options: Red, Orange, Yellow, Green, Teal, Blue, Indigo, Purple, Pink, Gray");
            MagicMaterialIconColor = Config.Bind("Item Colors", "Magic Crafting Material Icon Index", 5, "Indicates the color of the icon used for magic crafting materials. A number between 0 and 9. Available options: 0=Red, 1=Orange, 2=Yellow, 3=Green, 4=Teal, 5=Blue, 6=Indigo, 7=Purple, 8=Pink, 9=Gray");
            RareRarityColor = Config.Bind("Item Colors", "Rare Rarity Color", "Yellow", "The color of Rare rarity items, the second magic item tier. (Optional, use an HTML hex color starting with # to have a custom color.) Available options: Red, Orange, Yellow, Green, Teal, Blue, Indigo, Purple, Pink, Gray");
            RareMaterialIconColor = Config.Bind("Item Colors", "Rare Crafting Material Icon Index", 2, "Indicates the color of the icon used for rare crafting materials. A number between 0 and 9. Available options: 0=Red, 1=Orange, 2=Yellow, 3=Green, 4=Teal, 5=Blue, 6=Indigo, 7=Purple, 8=Pink, 9=Gray");
            EpicRarityColor = Config.Bind("Item Colors", "Epic Rarity Color", "Purple", "The color of Epic rarity items, the third magic item tier. (Optional, use an HTML hex color starting with # to have a custom color.) Available options: Red, Orange, Yellow, Green, Teal, Blue, Indigo, Purple, Pink, Gray");
            EpicMaterialIconColor = Config.Bind("Item Colors", "Epic Crafting Material Icon Index", 7, "Indicates the color of the icon used for epic crafting materials. A number between 0 and 9. Available options: 0=Red, 1=Orange, 2=Yellow, 3=Green, 4=Teal, 5=Blue, 6=Indigo, 7=Purple, 8=Pink, 9=Gray");
            LegendaryRarityColor = Config.Bind("Item Colors", "Legendary Rarity Color", "Teal", "The color of Legendary rarity items, the highest magic item tier. (Optional, use an HTML hex color starting with # to have a custom color.) Available options: Red, Orange, Yellow, Green, Teal, Blue, Indigo, Purple, Pink, Gray");
            LegendaryMaterialIconColor = Config.Bind("Item Colors", "Legendary Crafting Material Icon Index", 4, "Indicates the color of the icon used for legendary crafting materials. A number between 0 and 9. Available options: 0=Red, 1=Orange, 2=Yellow, 3=Green, 4=Teal, 5=Blue, 6=Indigo, 7=Purple, 8=Pink, 9=Gray");

            SetItemColor = Config.Bind("Item Colors", "Set Item Color", "#26ffff", "The color of set item text and the set item icon. Use a hex color, default is cyan");

            MagicItemEffectDefinitions.SetupMagicItemEffectDefinitions();

            LootTables.Clear();
            var lootConfig = LoadJsonFile<LootConfig>("loottables.json");
            AddLootTableConfig(lootConfig);
            PrintInfo();

            LoadAssets();

            ExtendedItemData.LoadExtendedItemData += SetupTestMagicItem;
            ExtendedItemData.LoadExtendedItemData += MagicItemComponent.OnNewExtendedItemData;
            ExtendedItemData.NewExtendedItemData += MagicItemComponent.OnNewExtendedItemData;

            _harmony = Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly(), PluginId);

            LootTableLoaded?.Invoke();
        }

        private void LoadAssets()
        {
            var assetBundle = LoadAssetBundle("epicloot");
            Assets.EquippedSprite = assetBundle.LoadAsset<Sprite>("Equipped");
            Assets.GenericSetItemSprite = assetBundle.LoadAsset<Sprite>("GenericSetItemMarker");
            Assets.GenericItemBgSprite = assetBundle.LoadAsset<Sprite>("GenericItemBg");
            Assets.MagicItemLootBeamPrefabs[(int)ItemRarity.Magic] = assetBundle.LoadAsset<GameObject>("MagicLootBeam");
            Assets.MagicItemLootBeamPrefabs[(int)ItemRarity.Rare] = assetBundle.LoadAsset<GameObject>("RareLootBeam");
            Assets.MagicItemLootBeamPrefabs[(int)ItemRarity.Epic] = assetBundle.LoadAsset<GameObject>("EpicLootBeam");
            Assets.MagicItemLootBeamPrefabs[(int)ItemRarity.Legendary] = assetBundle.LoadAsset<GameObject>("LegendaryLootBeam");

            LoadCraftingMaterialAssets(assetBundle, "Runestone");

            LoadCraftingMaterialAssets(assetBundle, "Shard");
            LoadCraftingMaterialAssets(assetBundle, "Dust");
            LoadCraftingMaterialAssets(assetBundle, "Reagent");
            LoadCraftingMaterialAssets(assetBundle, "Essence");

            LoadStationExtension(assetBundle, "piece_enchanter", new PieceDef()
            {
                Table = "_HammerPieceTable",
                CraftingStation = "piece_workbench",
                ExtendStation = "forge",
                Resources = new List<RecipeRequirementConfig>
                {
                    new RecipeRequirementConfig { item = "Stone", amount = 10 },
                    new RecipeRequirementConfig { item = "SurtlingCore", amount = 3 },
                    new RecipeRequirementConfig { item = "Copper", amount = 3 },
                }
            });
            LoadStationExtension(assetBundle, "piece_augmenter", new PieceDef()
            {
                Table = "_HammerPieceTable",
                CraftingStation = "piece_workbench",
                ExtendStation = "forge",
                Resources = new List<RecipeRequirementConfig>
                {
                    new RecipeRequirementConfig { item = "Obsidian", amount = 10 },
                    new RecipeRequirementConfig { item = "Crystal", amount = 3 },
                    new RecipeRequirementConfig { item = "Bronze", amount = 3 },
                }
            });
        }

        private static void LoadStationExtension(AssetBundle assetBundle, string assetName, PieceDef pieceDef)
        {
            var prefab = assetBundle.LoadAsset<GameObject>(assetName);
            RegisteredPieces.Add(prefab, pieceDef);
            RegisteredPrefabs.Add(prefab);
        }

        private static void LoadCraftingMaterialAssets(AssetBundle assetBundle, string type)
        {
            var prefabs = new GameObject[4];
            foreach (ItemRarity rarity in Enum.GetValues(typeof(ItemRarity)))
            {
                var assetName = $"{type}{rarity}";
                var prefab = assetBundle.LoadAsset<GameObject>(assetName);
                if (prefab == null)
                {
                    Debug.LogError($"Tried to load asset {assetName} but it does not exist in the asset bundle!");
                    continue;
                }
                prefabs[(int) rarity] = prefab;
                RegisteredPrefabs.Add(prefab);
                RegisteredItemPrefabs.Add(prefab);
            }
            Assets.CraftingMaterialPrefabs.Add(type, prefabs);
        }

        private static void SetupTestMagicItem(ExtendedItemData itemdata)
        {
            // Weapon (Club)
            /*if (itemdata.GetUniqueId() == "1493f9a4-65b4-41e3-8871-611ec8cb7564")
            {
                var magicItem = new MagicItem {Rarity = ItemRarity.Epic};
                magicItem.Effects.Add(RollEffect(MagicItemEffectDefinitions.Get(MagicEffectType.ModifyAttackStaminaUse), magicItem.Rarity));
                magicItem.Effects.Add(RollEffect(MagicItemEffectDefinitions.Get(MagicEffectType.Indestructible), magicItem.Rarity));
                magicItem.Effects.Add(RollEffect(MagicItemEffectDefinitions.Get(MagicEffectType.Weightless), magicItem.Rarity));
                //var magicItem = RollMagicItem(new LootDrop() { Rarity = { 6, 4, 2, 1 } }, itemdata);
                itemdata.ReplaceComponent<MagicItemComponent>().SetMagicItem(magicItem);
            }
            // Armor (Bronze Cuirass)
            else if (itemdata.GetUniqueId() == "84c006c7-3819-463c-b3b6-cb812f184655")
            {
                var magicItem = new MagicItem {Rarity = ItemRarity.Epic };
                //var magicItem = RollMagicItem(new LootDrop() { Rarity = { 6, 4, 2, 1 } }, itemdata);
                magicItem.Effects.Add(RollEffect(MagicItemEffectDefinitions.Get(MagicEffectType.ModifyMovementSpeed), magicItem.Rarity));
                magicItem.Effects.Add(RollEffect(MagicItemEffectDefinitions.Get(MagicEffectType.Indestructible), magicItem.Rarity));
                magicItem.Effects.Add(RollEffect(MagicItemEffectDefinitions.Get(MagicEffectType.Weightless), magicItem.Rarity));
                magicItem.Effects.Add(RollEffect(MagicItemEffectDefinitions.Get(MagicEffectType.AddCarryWeight), magicItem.Rarity));
                itemdata.ReplaceComponent<MagicItemComponent>().SetMagicItem(magicItem);
            }
            // Shield (Wood Shield)
            else if (itemdata.GetUniqueId() == "c0d8fb31-04dd-4499-b347-d0484416f159")
            {
                var magicItem = new MagicItem {Rarity = ItemRarity.Epic};
                magicItem.Effects.Add(RollEffect(MagicItemEffectDefinitions.Get(MagicEffectType.ModifyBlockStaminaUse), magicItem.Rarity));
                magicItem.Effects.Add(RollEffect(MagicItemEffectDefinitions.Get(MagicEffectType.Weightless), magicItem.Rarity));
                //var magicItem = RollMagicItem(new LootDrop() { Rarity = { 6, 4, 2, 1 } }, itemdata);
                itemdata.ReplaceComponent<MagicItemComponent>().SetMagicItem(magicItem);
            }
            // Legs (Troll Hide Legs)
            else if (itemdata.GetUniqueId() == "ec539738-6a73-492b-85d8-ce80eb0944f1")
            {
                var magicItem = new MagicItem { Rarity = ItemRarity.Epic };
                magicItem.Effects.Add(RollEffect(MagicItemEffectDefinitions.Get(MagicEffectType.ModifyMovementSpeed), magicItem.Rarity));
                magicItem.Effects.Add(RollEffect(MagicItemEffectDefinitions.Get(MagicEffectType.ModifySprintStaminaUse), magicItem.Rarity));
                magicItem.Effects.Add(RollEffect(MagicItemEffectDefinitions.Get(MagicEffectType.ModifyJumpStaminaUse), magicItem.Rarity));
                magicItem.Effects.Add(RollEffect(MagicItemEffectDefinitions.Get(MagicEffectType.AddCarryWeight), magicItem.Rarity));
                itemdata.ReplaceComponent<MagicItemComponent>().SetMagicItem(magicItem);
            }*/
        }

        private void OnDestroy()
        {
            ExtendedItemData.LoadExtendedItemData -= SetupTestMagicItem;
            _harmony?.UnpatchAll(PluginId);
        }

        public static void AddLootTableConfig(LootConfig lootConfig)
        {
            foreach (var lootTable in lootConfig.LootTables)
            {
                AddLootTable(lootTable);
            }
        }

        public static void AddLootTable(LootTable lootTable)
        {
            var key = lootTable.Object;
            if (string.IsNullOrEmpty(key) || lootTable.Loot.Length == 0 || lootTable.Drops.Length == 0)
            {
                return;
            }

            Debug.Log($"Added LootTable: {key}");
            if (!LootTables.ContainsKey(key))
            {
                LootTables.Add(key, new List<LootTable>());
            }

            LootTables[key].Add(lootTable);
        }

        public static void TryRegisterPrefabs(ZNetScene zNetScene)
        {
            if (zNetScene == null)
            {
                return;
            }

            foreach (var prefab in RegisteredPrefabs)
            {
                if (!zNetScene.m_prefabs.Contains(prefab))
                {
                    zNetScene.m_prefabs.Add(prefab);
                }
            }
        }

        public static void TryRegisterPieces(List<PieceTable> pieceTables, List<CraftingStation> craftingStations)
        {
            foreach (var entry in RegisteredPieces)
            {
                var prefab = entry.Key;
                var pieceDef = entry.Value;

                var piece = prefab.GetComponent<Piece>();

                var pieceTable = pieceTables.Find(x => x.name == pieceDef.Table);
                if (pieceTable.m_pieces.Contains(prefab))
                {
                    continue;
                }
                pieceTable.m_pieces.Add(prefab);

                var pieceStation = craftingStations.Find(x => x.name == pieceDef.CraftingStation);
                piece.m_craftingStation = pieceStation;

                var resources = new List<Piece.Requirement>();
                foreach (var resource in pieceDef.Resources)
                {
                    var resourcePrefab = ObjectDB.instance.GetItemPrefab(resource.item);
                    resources.Add(new Piece.Requirement()
                    {
                        m_resItem = resourcePrefab.GetComponent<ItemDrop>(),
                        m_amount = resource.amount
                    });
                }
                piece.m_resources = resources.ToArray();

                var stationExt = prefab.GetComponent<StationExtension>();
                if (stationExt != null && !string.IsNullOrEmpty(pieceDef.ExtendStation))
                {
                    var stationPrefab = pieceTable.m_pieces.Find(x => x.name == pieceDef.ExtendStation);
                    if (stationPrefab != null)
                    {
                        var station = stationPrefab.GetComponent<CraftingStation>();
                        stationExt.m_craftingStation = station;
                    }

                    var otherExt = pieceTable.m_pieces.Find(x => x.GetComponent<StationExtension>() != null);
                    if (otherExt != null)
                    {
                        var otherStationExt = otherExt.GetComponent<StationExtension>();
                        var otherPiece = otherExt.GetComponent<Piece>();

                        stationExt.m_connectionPrefab = otherStationExt.m_connectionPrefab;
                        piece.m_placeEffect.m_effectPrefabs = otherPiece.m_placeEffect.m_effectPrefabs.ToArray();
                    }
                }
                else
                {
                    var otherPiece = pieceTable.m_pieces.Find(x => x.GetComponent<Piece>() != null).GetComponent<Piece>();
                    piece.m_placeEffect.m_effectPrefabs.AddRangeToArray(otherPiece.m_placeEffect.m_effectPrefabs);
                }
            }
        }

        public static void TryRegisterItems()
        {
            if (ObjectDB.instance == null || ObjectDB.instance.m_items.Count == 0)
            {
                return;
            }
            
            foreach (var prefab in RegisteredItemPrefabs)
            {
                var itemDrop = prefab.GetComponent<ItemDrop>();
                if (itemDrop != null)
                {
                    if (ObjectDB.instance.GetItemPrefab(prefab.name.GetStableHashCode()) == null)
                    {
                        ObjectDB.instance.m_items.Add(prefab);
                    }
                }
            }

            var pieceTables = new List<PieceTable>();
            foreach (var itemPrefab in ObjectDB.instance.m_items)
            {
                var item = itemPrefab.GetComponent<ItemDrop>().m_itemData;
                if (item.m_shared.m_buildPieces != null && !pieceTables.Contains(item.m_shared.m_buildPieces))
                {
                    pieceTables.Add(item.m_shared.m_buildPieces);
                }
            }

            var craftingStations = new List<CraftingStation>();
            foreach (var pieceTable in pieceTables)
            {
                craftingStations.AddRange(pieceTable.m_pieces
                    .Where(x => x.GetComponent<CraftingStation>() != null)
                    .Select(x => x.GetComponent<CraftingStation>()));
            }

            TryRegisterPieces(pieceTables, craftingStations);
        }

        public static void TryRegisterRecipes()
        {
            Recipes_Setup.SetupRecipes();
        }

        private static T LoadJsonFile<T>(string filename) where T : class
        {
            var jsonFileName = GetAssetPath(filename);
            if (!string.IsNullOrEmpty(jsonFileName))
            {
                var jsonFile = File.ReadAllText(jsonFileName);
                return JSON.ToObject<T>(jsonFile);
            }

            return null;
        }

        private static AssetBundle LoadAssetBundle(string filename)
        {
            var assetBundlePath = GetAssetPath(filename);
            if (!string.IsNullOrEmpty(assetBundlePath))
            {
                return AssetBundle.LoadFromFile(assetBundlePath);
            }

            return null;
        }

        private static string GetAssetPath(string assetName, bool ignoreErrors = false)
        {
            var assetFileName = Path.Combine(Paths.PluginPath, "EpicLoot", assetName);
            if (!File.Exists(assetFileName))
            {
                Assembly assembly = typeof(EpicLoot).Assembly;
                assetFileName = Path.Combine(Path.GetDirectoryName(assembly.Location), assetName);
                if (!File.Exists(assetFileName))
                {
                    Debug.LogError($"Could not find asset ({assetName})");
                    return null;
                }
            }

            return assetFileName;
        }

        public static bool CanBeMagicItem(ItemDrop.ItemData item)
        {
            return item != null && IsPlayerItem(item) && Nonstackable(item) && IsNotRestrictedItem(item) && AllowedMagicItemTypes.Contains(item.m_shared.m_itemType);
        }

        public static Sprite GetMagicItemBgSprite()
        {
            return Assets.GenericItemBgSprite;
        }

        private static bool IsNotRestrictedItem(ItemDrop.ItemData item)
        {
            // This is dumb, but it's the only way I can think of to do this
            return !RestrictedItemNames.Contains(item.m_shared.m_name);
        }

        private static bool Nonstackable(ItemDrop.ItemData item)
        {
            return item.m_shared.m_maxStackSize == 1;
        }

        private static bool IsPlayerItem(ItemDrop.ItemData item)
        {
            // WTF, this is the only thing I found different between player usable items and items that are only for enemies
            return item.m_shared.m_icons.Length > 0;
        }

        public static string GetCharacterCleanName(Character character)
        {
            return character.name.Replace("(Clone)", "").Trim();
        }

        public static void OnCharacterDeath(CharacterDrop characterDrop)
        {
            var characterName = GetCharacterCleanName(characterDrop.m_character);
            var level = characterDrop.m_character.GetLevel();
            var dropPoint = characterDrop.m_character.GetCenterPoint() + characterDrop.transform.TransformVector(characterDrop.m_spawnOffset);

            OnCharacterDeath(characterName, level, dropPoint);
        }

        public static void OnCharacterDeath(string characterName, int level, Vector3 dropPoint)
        {
            var lootTables = GetLootTable(characterName, level);
            if (lootTables != null && lootTables.Count > 0)
            {
                List<GameObject> loot = RollLootTableAndSpawnObjects(lootTables, characterName, dropPoint);
                Debug.Log($"Rolling on loot table: {characterName} (lvl {level}), spawned {loot.Count} items at drop point({dropPoint}).");
                DropItems(loot, dropPoint);
                foreach (var l in loot)
                {
                    var itemData = l.GetComponent<ItemDrop>().m_itemData;
                    var magicItem = itemData.GetMagicItem();
                    if (magicItem != null)
                    {
                        Debug.Log($"  - {itemData.m_shared.m_name} <{l.transform.position}>: {string.Join(", ", magicItem.Effects.Select(x => x.EffectType.ToString()))}");
                    }
                }
            }
            else
            {
                Debug.Log($"Could not find loot table for: {characterName} (lvl {level})");
            }
        }

        public static List<GameObject> RollLootTableAndSpawnObjects(List<LootTable> lootTables, string objectName, Vector3 dropPoint)
        {
            return RollLootTableInternal(lootTables, objectName, dropPoint, true);
        }

        public static List<GameObject> RollLootTableAndSpawnObjects(LootTable lootTable, string objectName, Vector3 dropPoint)
        {
            return RollLootTableInternal(lootTable, objectName, dropPoint, true);
        }

        public static List<ItemDrop.ItemData> RollLootTable(List<LootTable> lootTables, string objectName, Vector3 dropPoint)
        {
            var results = new List<ItemDrop.ItemData>();
            var gameObjects = RollLootTableInternal(lootTables, objectName, dropPoint, false);
            foreach (var itemObject in gameObjects)
            {
                results.Add(itemObject.GetComponent<ItemDrop>().m_itemData.Clone());
                Destroy(itemObject);
            }

            return results;
        }

        public static List<ItemDrop.ItemData> RollLootTable(LootTable lootTable, string objectName, Vector3 dropPoint)
        {
            return RollLootTable(new List<LootTable> {lootTable}, objectName, dropPoint);
        }

        private static List<GameObject> RollLootTableInternal(List<LootTable> lootTables, string objectName, Vector3 dropPoint, bool initializeObject)
        {
            var results = new List<GameObject>();
            foreach (var lootTable in lootTables)
            {
                results.AddRange(RollLootTableInternal(lootTable, objectName, dropPoint, initializeObject));
            }
            return results;
        }

        private static List<GameObject> RollLootTableInternal(LootTable lootTable, string objectName, Vector3 dropPoint, bool initializeObject)
        {
            var results = new List<GameObject>();

            _weightedDropCountTable.Setup(lootTable.Drops, dropPair => dropPair.Length == 2 ? dropPair[1] : 1);
            var dropCountRollResult = _weightedDropCountTable.Roll();
            var dropCount = dropCountRollResult.Length >= 1 ? dropCountRollResult[0] : 0;
            if (dropCount == 0)
            {
                return results;
            }

            _weightedLootTable.Setup(lootTable.Loot, x => x.Weight);
            var selectedDrops = _weightedLootTable.Roll(dropCount);

            foreach (var lootDrop in selectedDrops)
            {
                var itemPrefab = ObjectDB.instance.GetItemPrefab(lootDrop.Item);
                if (itemPrefab == null)
                {
                    Debug.LogError($"Tried to spawn loot ({lootDrop.Item}) for ({objectName}), but the item prefab was not found!");
                    continue;
                }

                var randomRotation = Quaternion.Euler(0.0f, Random.Range(0.0f, 360.0f), 0.0f);
                ZNetView.m_forceDisableInit = !initializeObject;
                var item = Instantiate(itemPrefab, dropPoint, randomRotation);
                ZNetView.m_forceDisableInit = false;
                item.AddComponent<GotDestroyed>();
                var itemDrop = item.GetComponent<ItemDrop>();
                if (!CanBeMagicItem(itemDrop.m_itemData))
                {
                    Debug.LogError($"Tried to spawn loot ({lootDrop.Item}) for ({objectName}), but the item type ({itemDrop.m_itemData.m_shared.m_itemType}) is not allowed as a magic item!");
                    continue;
                }

                var itemData = new ExtendedItemData(itemDrop.m_itemData);
                var magicItemComponent = itemData.AddComponent<MagicItemComponent>();
                var magicItem = RollMagicItem(lootDrop, itemData);
                magicItemComponent.SetMagicItem(magicItem);

                itemDrop.m_itemData = itemData;

                InitializeMagicItem(itemData, magicItem);
                results.Add(item);
                MagicItemGenerated?.Invoke(itemData, magicItem);
            }

            return results;
        }

        public static MagicItem RollMagicItem(LootDrop lootDrop, ExtendedItemData baseItem)
        {
            var rarity = RollItemRarity(lootDrop);
            return RollMagicItem(rarity, baseItem);
        }

        public static MagicItem RollMagicItem(ItemRarity rarity, ExtendedItemData baseItem)
        {
            var magicItem = new MagicItem { Rarity = rarity };

            var effectCount = RollEffectCountPerRarity(magicItem.Rarity);
            for (int i = 0; i < effectCount; i++)
            {
                var availableEffects = MagicItemEffectDefinitions.GetAvailableEffects(baseItem, magicItem);
                if (availableEffects.Count == 0)
                {
                    Debug.LogWarning($"Tried to add more effects to magic item ({baseItem.m_shared.m_name}) but there were no more available effects. " +
                                     $"Current Effects: {(string.Join(", ", magicItem.Effects.Select(x => x.EffectType.ToString())))}");
                    break;
                }

                _weightedEffectTable.Setup(availableEffects, x => x.SelectionWeight);
                var effectDef = _weightedEffectTable.Roll();

                var effect = RollEffect(effectDef, magicItem.Rarity);
                magicItem.Effects.Add(effect);
            }
            
            return magicItem;
        }

        private static void InitializeMagicItem(ExtendedItemData baseItem, MagicItem magicItem)
        {
            if (baseItem.m_shared.m_useDurability)
            {
                baseItem.m_durability = Random.Range(0.2f, 1.0f) * baseItem.GetMaxDurability();
            }
        }

        public static int RollEffectCountPerRarity(ItemRarity rarity)
        {
            Dictionary<int, float> countPercents = MagicEffectCountWeightsPerRarity[rarity];
            _weightedEffectCountTable.Setup(countPercents, x => x.Value);
            return _weightedEffectCountTable.Roll().Key;
        }

        public static MagicItemEffect RollEffect(MagicItemEffectDefinition effectDef, ItemRarity itemRarity)
        {
            var valuesDef = effectDef.ValuesPerRarity[itemRarity];
            float value = valuesDef.MinValue;
            if (valuesDef.Increment != 0)
            {
                int incrementCount = (int)((valuesDef.MaxValue - valuesDef.MinValue) / valuesDef.Increment);
                value = valuesDef.MinValue + (Random.Range(0, incrementCount + 1) * valuesDef.Increment);
            }

            return new MagicItemEffect()
            {
                EffectType = effectDef.Type,
                EffectValue = value
            };
        }

        public static ItemRarity RollItemRarity(LootDrop lootDrop)
        {
            if (lootDrop.Rarity == null || lootDrop.Rarity.Length == 0)
            {
                return ItemRarity.Magic;
            }

            Dictionary<ItemRarity, int> rarityWeights = new Dictionary<ItemRarity, int>()
            {
                { ItemRarity.Magic, lootDrop.Rarity.Length >= 1 ? lootDrop.Rarity[0] : 0 },
                { ItemRarity.Rare, lootDrop.Rarity.Length >= 2 ? lootDrop.Rarity[1] : 0 },
                { ItemRarity.Epic, lootDrop.Rarity.Length >= 3 ? lootDrop.Rarity[2] : 0 },
                { ItemRarity.Legendary, lootDrop.Rarity.Length >= 4 ? lootDrop.Rarity[3] : 0 }
            };

            _weightedRarityTable.Setup(rarityWeights, x => x.Value);
            return _weightedRarityTable.Roll().Key;
        }
        

        public static void DropItems(List<GameObject> loot, Vector3 centerPos, float dropHemisphereRadius = 0.5f)
        {
            foreach (var item in loot)
            {
                var vector3 = Random.insideUnitSphere * dropHemisphereRadius;
                vector3.y = Mathf.Abs(vector3.y);
                item.transform.position = centerPos + vector3;
                item.transform.rotation = Quaternion.Euler(0.0f, Random.Range(0, 360), 0.0f);

                var rigidbody = item.GetComponent<Rigidbody>();
                if (rigidbody != null)
                {
                    var insideUnitSphere = Random.insideUnitSphere;
                    if (insideUnitSphere.y < 0.0)
                    {
                        insideUnitSphere.y = -insideUnitSphere.y;
                    }
                    rigidbody.AddForce(insideUnitSphere * 5f, ForceMode.VelocityChange);
                }
            }
        }

        public static List<LootTable> GetLootTable(string objectName, int level)
        {
            var results = new List<LootTable>();
            if (LootTables.TryGetValue(objectName, out List<LootTable> lootTables))
            {
                foreach (var lootTable in lootTables)
                {
                    results.Add(GenerateLootTableForLevel(lootTable, level));
                }
            }
            return results;
        }

        public static LootTable GenerateLootTableForLevel(LootTable lootTable, int level)
        {
            var result = new LootTable()
            {
                Object = lootTable.Object
            };

            // Use only the level-specific drops
            if (level == 2 && !ArrayUtils.IsNullOrEmpty(lootTable.Drops2))
            {
                result.Drops = lootTable.Drops2.ToArray();
            }
            else if (level == 3 && !ArrayUtils.IsNullOrEmpty(lootTable.Drops3))
            {
                result.Drops = lootTable.Drops3.ToArray();
            }
            else
            {
                result.Drops = lootTable.Drops.ToArray();
            }

            // Combine all the loot up to the level
            var allLoot = new List<LootDrop>();
            allLoot.AddRange(lootTable.Loot);
            if (level >= 2 && !ArrayUtils.IsNullOrEmpty(lootTable.Loot2))
            {
                allLoot.AddRange(lootTable.Loot2);
            }
            if (level >= 3 && !ArrayUtils.IsNullOrEmpty(lootTable.Loot3))
            {
                allLoot.AddRange(lootTable.Loot3);
            }
            result.Loot = allLoot.ToArray();

            return result;
        }

        private void PrintInfo()
        {
            var t = new StringBuilder();
            t.AppendLine($"# EpicLoot Data v{Version}");
            t.AppendLine();
            t.AppendLine("*Author: RandyKnapp*");
            t.AppendLine("*Source: [Github](https://github.com/RandyKnapp/ValheimMods/tree/main/EpicLoot)*");
            t.AppendLine();

            // Magic item effects per rarity
            t.AppendLine("# Magic Effect Count Weights Per Rarity");
            t.AppendLine();
            t.AppendLine("Each time a **MagicItem** is rolled a number of **MagicItemEffects** are added based on its **Rarity**. The percent chance to roll each number of effects is found on the following table. These values are hardcoded.");
            t.AppendLine();
            t.AppendLine("The raw weight value is shown first, followed by the calculated percentage chance in parentheses.");
            t.AppendLine();
            t.AppendLine("|Rarity|1|2|3|4|5|6|");
            t.AppendLine("|--|--|--|--|--|--|--|");
            t.AppendLine(GetMagicEffectCountTableLine(ItemRarity.Magic));
            t.AppendLine(GetMagicEffectCountTableLine(ItemRarity.Rare));
            t.AppendLine(GetMagicEffectCountTableLine(ItemRarity.Epic));
            t.AppendLine(GetMagicEffectCountTableLine(ItemRarity.Legendary));
            t.AppendLine();

            // Magic item effects
            t.AppendLine("# MagicItemEffect List");
            t.AppendLine();
            t.AppendLine("The following lists all the built-in **MagicItemEffects**. MagicItemEffects are hardcoded in `MagicItemEffectDefinitions_Setup.cs` and " +
                         "added to `MagicItemEffectDefinitions`. EpicLoot uses an enum for the types of magic effects, but the backing field underneath is an int. " +
                         "You can add your own new types using your own enum that starts after `MagicEffectType.MagicEffectEnumEnd` and cast it to `MagicEffectType` " +
                         "or use your own range of int identifiers.");
            t.AppendLine();
            t.AppendLine("Listen to the event `MagicItemEffectDefinitions.OnSetupMagicItemEffectDefinitions` (which gets called in `EpicLoot.Awake`) to add your own.");
            t.AppendLine();
            t.AppendLine("The int value of the type is displayed in parentheses after the name.");
            t.AppendLine();
            t.AppendLine("  * **Display Text:** This text appears in the tooltip for the magic item, with {0:?} replaced with the rolled value for the effect, formatted using the shown C# string format.");
            t.AppendLine("  * **Allowed Item Types:** This effect may only be rolled on items of a the types in this list. When this list is empty, this is usually done because this is a special effect type added programmatically  or currently not allowed to roll.");
            t.AppendLine("  * **Requirement:** A function called when attempting to add this effect to an item. The `Requirement` function must return true for this effect to be able to be added to this magic item.");
            t.AppendLine("  * **Value Per Rarity:** This effect may only be rolled on items of a rarity included in this table. The value is rolled using a linear distribution between Min and Max and divisible by the Increment.");
            t.AppendLine();
            t.AppendLine("Some lists of effect types are used in requirements to consolidate code. They are: PhysicalDamageEffects, ElementalDamageEffects, and AllDamageEffects. Included here for your reference:");
            t.AppendLine();
            t.AppendLine("  * **`PhysicalDamageEffects`:** AddBluntDamage, AddSlashingDamage, AddPiercingDamage");
            t.AppendLine("  * **`ElementalDamageEffects`:** AddFireDamage, AddFrostDamage, AddLightningDamage");
            t.AppendLine("  * **`AllDamageEffects`:** AddBluntDamage, AddSlashingDamage, AddPiercingDamage, AddFireDamage, AddFrostDamage, AddLightningDamage, AddPoisonDamage, AddSpiritDamage");
            t.AppendLine();

            Dictionary<string, string> requirementSource = null;
            const string devSourceFile = @"C:\Users\rknapp\Documents\GitHub\ValheimMods\EpicLoot\MagicItemEffectDefinitions_Setup.cs";
            if (File.Exists(devSourceFile))
            {
                requirementSource = ParseSource(File.ReadLines(devSourceFile));
            }

            foreach (var definitionEntry in MagicItemEffectDefinitions.AllDefinitions)
            {
                var def = definitionEntry.Value;
                t.AppendLine($"## {def.Type} ({def.IntType})");
                t.AppendLine();
                t.AppendLine($"> **Display Text:** {def.DisplayText}");
                t.AppendLine("> ");
                t.AppendLine("> **Allowed Item Types:** " + (def.AllowedItemTypes.Count == 0 ? "*None*" : string.Join(", ", def.AllowedItemTypes)));

                if (requirementSource != null && requirementSource.ContainsKey(def.Type.ToString()))
                {
                    t.AppendLine("> ");
                    t.AppendLine("> **Requirement:**");
                    t.AppendLine("> ```");
                    t.AppendLine($"> {requirementSource[def.Type.ToString()]}");
                    t.AppendLine("> ```");
                    t.AppendLine("> ");
                }

                if (def.ValuesPerRarity.Count > 0)
                {
                    t.AppendLine("> ");
                    t.AppendLine("> **Value Per Rarity:**");
                    t.AppendLine("> ");
                    t.AppendLine("> |Rarity|Min|Max|Increment|");
                    t.AppendLine("> |--|--|--|--|");
                    foreach (var entry in def.ValuesPerRarity)
                    {
                        var v = entry.Value;
                        t.AppendLine($"> |{entry.Key}|{v.MinValue}|{v.MaxValue}|{v.Increment}|");
                    }
                }
                if (!string.IsNullOrEmpty(def.Comment))
                {
                    t.AppendLine("> ");
                    t.AppendLine($"> ***Notes:*** *{def.Comment}*");
                }

                t.AppendLine();
            }

            // Loot tables
            t.AppendLine("# Loot Tables");
            t.AppendLine();
            t.AppendLine("A list of every built-in loot table from the mod. The name of the loot table is the object name followed by a number signifying the level of the object.");

            foreach (var lootTableEntry in LootTables)
            {
                var list = lootTableEntry.Value;

                foreach (var lootTable in list)
                {
                    t.AppendLine($"## {lootTableEntry.Key}");
                    t.AppendLine();
                    WriteLootTableDrops(t, lootTable);
                    WriteLootTableItems(t, lootTable);
                    t.AppendLine();
                }
            }

            //var outputFilePath = Path.Combine(Path.GetDirectoryName(typeof(EpicLoot).Assembly.Location), "info.md");
            //File.WriteAllText(outputFilePath, t.ToString());

            const string devOutputPath = @"C:\Users\rknapp\Documents\GitHub\ValheimMods\EpicLoot";
            if (Directory.Exists(devOutputPath))
            {
                File.WriteAllText(Path.Combine(devOutputPath, "info.md"), t.ToString());
            }
        }

        private static void WriteLootTableDrops(StringBuilder t, LootTable lootTable)
        {
            var dropTables = new[] { lootTable.Drops, lootTable.Drops2, lootTable.Drops3 };
            for (var i = 0; i < 3; i++)
            {
                var levelDisplay = $" (lvl {i + 1})";
                if (i == 0 && ArrayUtils.IsNullOrEmpty(lootTable.Drops2) && ArrayUtils.IsNullOrEmpty(lootTable.Drops3))
                {
                    levelDisplay = "";
                }
                else if (i == 0 && ArrayUtils.IsNullOrEmpty(lootTable.Drops2))
                {
                    levelDisplay = " (lvl 1, 2)";
                }
                else if (i == 0 && ArrayUtils.IsNullOrEmpty(lootTable.Drops3))
                {
                    levelDisplay = " (lvl 1, 3)";
                }

                var dropTable = dropTables[i];
                if (ArrayUtils.IsNullOrEmpty(dropTable))
                {
                    continue;
                }

                float total = lootTable.Drops.Sum(x => x.Length > 1 ? x[1] : 0);
                if (total > 0)
                {
                    t.AppendLine($"> | Drops{levelDisplay} | Weight (Chance) |");
                    t.AppendLine($"> | -- | -- |");
                    foreach (var drop in dropTable)
                    {
                        var count = drop.Length > 0 ? drop[0] : 0;
                        var value = drop.Length > 1 ? drop[1] : 0;
                        var percent = (value / total) * 100;
                        t.AppendLine($"> | {count} | {value} ({percent:0.#}%) |");
                    }
                }
                t.AppendLine();
            }
        }

        private static void WriteLootTableItems(StringBuilder t, LootTable lootTable)
        {
            var lootLists = new[] { lootTable.Loot, lootTable.Loot2, lootTable.Loot3 };

            for (var i = 0; i < 3; i++)
            {
                var levelDisplay = $" (lvl {i + 1}+)";
                if (i == 0 && ArrayUtils.IsNullOrEmpty(lootTable.Loot2) && ArrayUtils.IsNullOrEmpty(lootTable.Loot3))
                {
                    levelDisplay = "";
                }

                var lootList = lootLists[i];
                if (ArrayUtils.IsNullOrEmpty(lootList))
                {
                    continue;
                }

                t.AppendLine($"> | Items{levelDisplay} | Weight (Chance) | Magic | Rare | Epic | Legendary |");
                t.AppendLine("> | -- | -- | -- | -- | -- | -- |");

                float totalLootWeight = lootList.Sum(x => x.Weight);
                foreach (var lootDrop in lootList)
                {
                    var percentChance = lootDrop.Weight / totalLootWeight * 100;
                    if (lootDrop.Rarity == null || lootDrop.Rarity.Length == 0)
                    {
                        t.AppendLine($"> | {lootDrop.Item} | {lootDrop.Weight} ({percentChance:0.#}%) | 1 (100%) | 0 (0%) | 0 (0%) | 0 (0%) |");
                        continue;
                    }

                    float rarityTotal = lootDrop.Rarity.Sum();
                    float[] rarityPercent =
                    {
                        lootDrop.Rarity[0] / rarityTotal * 100,
                        lootDrop.Rarity[1] / rarityTotal * 100,
                        lootDrop.Rarity[2] / rarityTotal * 100,
                        lootDrop.Rarity[3] / rarityTotal * 100,
                    };
                    t.AppendLine($"> | {lootDrop.Item} | {lootDrop.Weight} ({percentChance:0.#}%) " +
                                 $"| {lootDrop.Rarity[0]} ({rarityPercent[0]:0.#}%) " +
                                 $"| {lootDrop.Rarity[1]} ({rarityPercent[1]:0.#}%) " +
                                 $"| {lootDrop.Rarity[2]:0.#} ({rarityPercent[2]:0.#}%) " +
                                 $"| {lootDrop.Rarity[3]} ({rarityPercent[3]:0.#}%) |");
                }

                t.AppendLine();
            }
        }

        private static Dictionary<string, string> ParseSource(IEnumerable<string> lines)
        {
            var results = new Dictionary<string, string>();
            var currentType = "";
            foreach (var sourceLine in lines)
            {
                var line = sourceLine.Trim();
                if (string.IsNullOrEmpty(currentType))
                {
                    if (line.StartsWith("Type = "))
                    {
                        var start = line.IndexOf(".", StringComparison.InvariantCulture);
                        var end = line.IndexOf(",", StringComparison.InvariantCulture);
                        if (start < 0 || end < 0)
                        {
                            continue;
                        }

                        start += 1;
                        currentType = line.Substring(start, end - start);
                    }
                }
                else
                {
                    if (line.StartsWith("});") || line.StartsWith("Add("))
                    {
                        currentType = "";
                    }
                    else if (line.StartsWith("Requirement"))
                    {
                        var start = ("Requirement = ").Length;
                        var end = line.Length - 1;
                        var requirementText = line.Substring(start, end - start);
                        results.Add(currentType, requirementText);
                    }
                }
            }

            return results;
        }

        private static string GetMagicEffectCountTableLine(ItemRarity rarity)
        {
            var effectCounts = MagicEffectCountWeightsPerRarity[rarity];
            var total = effectCounts.Sum(x => x.Value);
            var result = $"|{rarity}|";
            for (int i = 1; i <= 6; ++i)
            {
                var valueString = " ";
                if (effectCounts.TryGetValue(i, out float value))
                {
                    var percent = value / total * 100;
                    valueString = $"{value} ({percent:0.#}%)";
                }
                result += $"{valueString}|";
            }
            return result;
        }

        public static string GetSetItemColor()
        {
            return SetItemColor.Value;
        }

        public static string GetRarityColor(ItemRarity rarity)
        {
            switch (rarity)
            {
                case ItemRarity.Magic:
                    return GetColor(MagicRarityColor.Value);
                case ItemRarity.Rare:
                    return GetColor(RareRarityColor.Value);
                case ItemRarity.Epic:
                    return GetColor(EpicRarityColor.Value);
                case ItemRarity.Legendary:
                    return GetColor(LegendaryRarityColor.Value);
                default:
                    throw new ArgumentOutOfRangeException(nameof(rarity), rarity, null);
            }
        }

        public static Color GetRarityColorARGB(ItemRarity rarity)
        {
            return ColorUtility.TryParseHtmlString(GetRarityColor(rarity), out var color) ? color : Color.white;
        }

        private static string GetColor(string configValue)
        {
            if (configValue.StartsWith("#"))
            {
                return configValue;
            }
            else
            {
                if (MagicItemColors.TryGetValue(configValue, out var color))
                {
                    return color;
                }
            }

            return "#000000";
        }

        public static int GetRarityIconIndex(ItemRarity rarity)
        {
            switch (rarity)
            {
                case ItemRarity.Magic:
                    return Mathf.Clamp(MagicMaterialIconColor.Value, 0, 9);
                case ItemRarity.Rare:
                    return Mathf.Clamp(RareMaterialIconColor.Value, 0, 9);
                case ItemRarity.Epic:
                    return Mathf.Clamp(EpicMaterialIconColor.Value, 0, 9);
                case ItemRarity.Legendary:
                    return Mathf.Clamp(LegendaryMaterialIconColor.Value, 0, 9);
                default:
                    throw new ArgumentOutOfRangeException(nameof(rarity), rarity, null);
            }
        }
    }
}
