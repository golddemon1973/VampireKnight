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

        private Menu _menuRef;

        public override string GetVersion() => "26.4.4.0";
        public new string GetName() => "Vampire Knight";

        AudioClip CarefreeSFX;
        AudioClip BaldurShellSFX;
        AudioClip LifebloodHitSFX;

        public override void Initialize(Dictionary<string, Dictionary<string, GameObject>> preloadedObjects)
        {
            Instance = this;

            HookAll();

            Log("[INFO] :: (Hooked events, loaded assets and started drain coroutine)");
        }

        private IEnumerator LoadAssets()
        {
            Log("[INFO] :: (Loading assets...)");

            CarefreeSFX = Resources.FindObjectsOfTypeAll<AudioClip>()
.           FirstOrDefault(x => x.name == "carefree_melody_metallic");

            if (CarefreeSFX != null)
            {
                Log("[INFO] :: (Found asset 'carefree_melody_metallic'; Upon player maskgain sound)");
            }
            else
            {
                yield return new WaitForSeconds(1);

                LogWarn("[WARN] :: (Couldn't find asset 'carefree_melody_metallic'; retrying soon)");

                CarefreeSFX = Resources.FindObjectsOfTypeAll<AudioClip>()
                .FirstOrDefault(x => x.name == "carefree_melody_metallic");

                if (CarefreeSFX != null)
                {
                    Log("[INFO] :: (Found asset 'carefree_melody_metallic' on second attempt; Upon player maskgain sound)");
                }
                else
                {
                    LogWarn("[WARN] :: (Still couldn't find 'carefree_melody_metallic' on second attempt; will not play sound)");
                }
            }

            BaldurShellSFX = Resources.FindObjectsOfTypeAll<AudioClip>()
           .FirstOrDefault(x => x.name == "hero_blocker_charm_block");

            if (BaldurShellSFX != null)
            {
                Log("[INFO] :: (Found asset 'hero_blocker_charm_block'; Upon player maskloss sound)");
            }
            else
            {
                yield return new WaitForSeconds(1);

                LogWarn("[WARN] :: (Couldn't find asset 'hero_blocker_charm_block'; retrying soon)");

                BaldurShellSFX = Resources.FindObjectsOfTypeAll<AudioClip>()
                .FirstOrDefault(x => x.name == "hero_blocker_charm_block");

                if (BaldurShellSFX != null)
                {
                    Log("[INFO] :: (Found asset 'hero_blocker_charm_block' on second attempt; Upon player maskloss sound)");
                }
                else
                {
                    LogWarn("[WARN] :: (Still couldn't find 'hero_blocker_charm_block' on second attempt; will not play sound)");
                }
            }

            //health_cocoon_break

            LifebloodHitSFX = Resources.FindObjectsOfTypeAll<AudioClip>()
            .FirstOrDefault(x => x.name == "health_cocoon_break");

            if (LifebloodHitSFX != null)
            {
                Log("[INFO] :: (Found asset 'health_cocoon_break'; Upon player maskloss sound)");
            }
            else
            {
                yield return new WaitForSeconds(1);

                LogWarn("[WARN] :: (Couldn't find asset 'health_cocoon_break'; retrying soon)");

                LifebloodHitSFX = Resources.FindObjectsOfTypeAll<AudioClip>()
                .FirstOrDefault(x => x.name == "health_cocoon_break");

                if (LifebloodHitSFX != null)
                {
                    Log("[INFO] :: (Found asset 'health_cocoon_break' on second attempt; Upon player maskloss sound)");
                }
                else
                {
                    LogWarn("[WARN] :: (Still couldn't find 'health_cocoon_break' on second attempt; will not play sound)");
                }
            }
        }

        private void HookAll()
        {
            On.HealthManager.Hit += OnEnemyHitHook;

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

            GameManager.instance.StartCoroutine(Bloodloss());
            GameManager.instance.StartCoroutine(LoadAssets());
        }

        private bool IsEnemyVulnerable(HealthManager HealthManager)
        {
            var Collider = HealthManager.GetComponent<Collider2D>();

            if (Collider == null || !Collider.enabled) return false;

            var FSMComponent = HealthManager.gameObject.GetComponent<PlayMakerFSM>();

            if (FSMComponent != null)
            {
                var isInvincible = FSMComponent.FsmVariables.FindFsmBool("Is Invincible");

                if (isInvincible != null && isInvincible.Value) return false;
            }

            return true;
        }

        public bool IsAnyVulnerableEnemyAlive()
        {
            return UObject.FindObjectsOfType<HealthManager>().Any(Enemy =>
                Enemy != null &&
                !Enemy.isDead &&
                Enemy.gameObject.activeInHierarchy &&
                IsEnemyVulnerable(Enemy)
            );
        }

        private IEnumerator Bloodloss()
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

                // first checks

                if (!GS.VampireEnabled || !HeroController.instance.acceptingInput || PlayerData.instance.health == 0) continue;

                // second checks

                bool hasValidEnemy = IsAnyVulnerableEnemyAlive();

                if (!hasValidEnemy) continue;

                if (PlayerData.instance.healthBlue == 0 && PlayerData.instance.joniHealthBlue == 0)
                {
                    HeroController.instance.GetComponent<AudioSource>().PlayOneShot(BaldurShellSFX, 1f);
                }
                else
                {
                    HeroController.instance.GetComponent<AudioSource>().PlayOneShot(LifebloodHitSFX, 1f);
                }

                HeroController.instance.TakeHealth(Maskloss);

                if (PlayerData.instance.health <= Maskloss && !Kill && PlayerData.instance.health != 1)
                {
                    HeroController.instance.TakeHealth(PlayerData.instance.health - 1);
                } else if (PlayerData.instance.health <= Maskloss && Kill)
                {
                    HeroController.instance.TakeHealth(PlayerData.instance.health);
                    HeroController.instance.StartCoroutine("Die");
                }
            }

        }

        private void OnEnemyHitHook(On.HealthManager.orig_Hit orig, HealthManager self, HitInstance hitInstance)
        {
            orig(self, hitInstance);

            if (!GS.VampireEnabled) return;

            if (PlayerData.instance.health < PlayerData.instance.maxHealth)
            {
                HeroController.instance.AddHealth(1);
                HeroController.instance.GetComponent<AudioSource>().PlayOneShot(CarefreeSFX, 1f);
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
            {"BloodlossRate", 3},
            {"MasklossWhenBloodloss", 1},
            {"Kill", false}
        };

        private Dictionary<string, object> GetDifficultyOptions()
        {
            switch (GS.ModDifficulty)
            {
                case 0: return EasyDifficulty;
                case 1: return NormalDifficulty;
                case 2: return HardcoreDifficulty;
                case 3: return PantheonDifficulty;
                case 4:
                    return new Dictionary<string, object> 
                    {
                    {"BloodlossRate", GS.CustomBloodlossRate},
                    {"MasklossWhenBloodloss", GS.CustomMaskloss},
                    {"Kill", GS.CustomKill}
                    };
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

                new TextPanel("— Presets —"),

                new HorizontalOption(
                    name:         "Mod Difficulty",
                    description:  "Choose the difficulty of the mod.",
                    values:       new[] { "Easy", "Normal", "Hardcore", "Pantheon", "Custom"},
                    applySetting: index => GS.ModDifficulty = index,
                    loadSetting:  ()    => GS.ModDifficulty
                ),

                new TextPanel("— Custom —"),

                new CustomSlider(
                    name: "Custom Bloodloss",
                    val => GS.CustomBloodlossRate = (int)val,
                    () => GS.CustomBloodlossRate,
                    1f, 20f, true
                ),

                new CustomSlider(
                    name: "Custom Maskloss",
                    val => GS.CustomMaskloss = (int)val,
                    () => GS.CustomMaskloss,
                    1f, 5f, true
                ),

                Blueprints.HorizontalBoolOption(
                    name:         "Lethal Maskloss",
                    description:  "Whether or not you can die out of Maskloss.",
                    applySetting: val => GS.CustomKill = val,
                    loadSetting:  ()  => GS.CustomKill
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
