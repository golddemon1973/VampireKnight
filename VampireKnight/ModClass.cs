using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GlobalEnums;
using JetBrains.Annotations;
using Modding;
using Modding.Delegates;
using Satchel.BetterMenus;
using UnityEngine;
using static UnityEngine.Networking.UnityWebRequest;
using PlaymakerFSM = PlayMakerFSM;
using UObject = UnityEngine.Object;

namespace VampireKnight
{
    public class VampireKnight : Mod, ICustomMenuMod, IGlobalSettings<GlobalSettings>
    {
        internal static VampireKnight Instance;

        public static GlobalSettings GS { get; private set; } = new GlobalSettings();

        public void OnLoadGlobal(GlobalSettings s) => GS = s;
        public GlobalSettings OnSaveGlobal() => GS;

        private bool _vampireSwingHit = false;
        private Menu _menuRef;

        public override string GetVersion() => "1.2.0.36";

        AudioClip CarefreeSFX;
        AudioClip BaldurShellSFX;

        public override void Initialize(Dictionary<string, Dictionary<string, GameObject>> preloadedObjects)
        {
            Instance = this;

            HookAll();

            Log("[INFO] :: (Hooked events and started drain coroutine)");

            CarefreeSFX = Resources.FindObjectsOfTypeAll<AudioClip>()
            .FirstOrDefault(x => x.name == "carefree_melody_metallic");

            Log("[INFO] :: (Found asset 'carefree_melody_metallic'; Upon enemy hit sound)");

            BaldurShellSFX = Resources.FindObjectsOfTypeAll<AudioClip>()
           .FirstOrDefault(x => x.name == "shell_shield_hit");

            Log("[INFO] :: (Found asset 'shell_shield_hit'; Upon player maskloss sound)");
        }

        private void HookAll()
        {
            ModHooks.SlashHitHook += OnSlashHitHook;
            ModHooks.AfterAttackHook += ResetCooldown;
            On.HeroController.Awake += OnHeroAwake;
            On.HeroController.CanFocus += NoFocus;
        }

        private bool NoFocus(On.HeroController.orig_CanFocus orig, HeroController self)
        {
            if (GS.VampireEnabled) return false;

            return orig(self);
        }

        private void OnHeroAwake(On.HeroController.orig_Awake orig, HeroController self)
        {
            orig(self);

            GameManager.instance.StartCoroutine(SlowDrain());
        }

        private IEnumerator SlowDrain()
        {
            while (true)
            {
                Dictionary<string, object> DifficultyOps = GetDifficultyOptions();

                int BloodlossRate = DifficultyOps.TryGetValue("BloodlossRate", out object brRaw)
                ? (int)brRaw
                : 5; // default value incase of an unexpected error

                int Maskloss = DifficultyOps.TryGetValue("MasklossWhenBloodloss", out object mlRaw)
                ? (int)mlRaw
                : 1; // default value incase of an unexpected error

                bool Kill = DifficultyOps.TryGetValue("Kill", out object kRaw)
                ? (bool)kRaw
                : false; // default value incase of an unexpected error

                yield return new WaitForSeconds(BloodlossRate);

                if (!GS.VampireEnabled) continue;

                if (!HeroController.instance.acceptingInput) continue;

                if (PlayerData.instance.health == 0) continue;

                // this SUCKS i hate myself

                bool hasValidEnemy = UnityEngine.Object.FindObjectsOfType<HealthManager>()
                    .Any(hm => !hm.IsInvincible) && UnityEngine.Object.FindObjectsOfType<HealthManager>()
                    .Any(hm => !hm.isDead);

                if (!hasValidEnemy) continue;

                if (!Kill && (PlayerData.instance.health <= 1)) continue;

                if (PlayerData.instance.health <= Maskloss || PlayerData.instance.health == 1)
                {
                    HeroController.instance.TakeHealth(PlayerData.instance.health);
                    HeroController.instance.StartCoroutine("Die");

                    continue;
                }

                HeroController.instance.TakeHealth(Maskloss);
            }
        }

        private void ResetCooldown(AttackDirection Direction)
        {
            _vampireSwingHit = false;
        }

        private void OnSlashHitHook(Collider2D enemyCollider, GameObject enemyGameObject)
        {
            // this is now pretty optimized

            if (!GS.VampireEnabled) return;
            if (_vampireSwingHit) return;

            if (enemyCollider.gameObject.layer == 11)
            {
                HealthManager hm = enemyCollider.gameObject.GetComponent<HealthManager>()
                 ?? enemyCollider.gameObject.GetComponentInParent<HealthManager>();

                if (hm == null || hm.isDead) return;

                _vampireSwingHit = true;

                if (PlayerData.instance.health < PlayerData.instance.maxHealth)
                {
                    HeroController.instance.AddHealth(1);
                }
            }
        }

        Dictionary<string, object> EasyDifficulty = new() {
            {"BloodlossRate", 7},
            {"MasklossWhenBloodloss", 1},
            {"Kill", false}
        };

        Dictionary<string, object> NormalDifficulty = new() {
            {"BloodlossRate", 5},
            {"MasklossWhenBloodloss", 1},
            {"Kill", false}
        };

        Dictionary<string, object> HardcoreDifficulty = new() {
            {"BloodlossRate", 6},
            {"MasklossWhenBloodloss", 2},
            {"Kill", true}
        };

        Dictionary<string, object> PantheonDifficulty = new() {
            {"BloodlossRate", 2},
            {"MasklossWhenBloodloss", 1},
            {"Kill", true}
        };

        private Dictionary<string, object> GetDifficultyOptions()
        {
            switch (GS.ModDifficulty)
            {
                case 0: return EasyDifficulty;
                case 1: return NormalDifficulty;
                case 2: return HardcoreDifficulty;
                case 3: return PantheonDifficulty;
                default:
                    LogWarn("[WARN] :: (Difficulty index out of bounds; resetting to 1)");
                    GS.ModDifficulty = 1;
                    return NormalDifficulty;
            }
        }

        // i love satchel

        public bool ToggleButtonInsideMenu => false;

        public MenuScreen GetMenuScreen(MenuScreen modListMenu, ModToggleDelegates? toggleDelegates)
        {
            _menuRef ??= new Menu("VampireKnight", new Element[]
            {
                // blueprints.. like.. A BLUE SPY? A BLEU SPY'S IN TBHEZ BAZEZ I REPEATTTTTT

                new TextPanel("— General —"),

                Blueprints.HorizontalBoolOption(
                    name:         "Vampire Enabled",
                    description:  "Toggles the vampire mechanic on or off.",
                    applySetting: val => GS.VampireEnabled = val,
                    loadSetting:  ()  => GS.VampireEnabled
                ),

                new TextPanel("— Settings —"),

                new HorizontalOption(
                    name:         "Mod Difficulty",
                    description:  "Choose the difficulty of the mod.",
                    values:       new[] { "Easy", "Normal", "Hardcore", "Pantheon"},
                    applySetting: index => GS.ModDifficulty = index,
                    loadSetting:  ()    => GS.ModDifficulty
                ),

                new TextPanel("— Credits —"),
                new TextPanel("Author: ivory"),
                new TextPanel("Special Thanks: Charles Game Dev"),
                new TextPanel("Helped me figure out how to make this mod correctly through his scripts"),
                new TextPanel("Special Thanks: Satchel"),
                new TextPanel("Used Satchel's BetterMenus which made making menus alot easier"),
            });

            return _menuRef.GetMenuScreen(modListMenu);
        }
    }
}
