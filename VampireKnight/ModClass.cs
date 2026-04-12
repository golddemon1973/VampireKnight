using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using GlobalEnums;
using Modding;
using Modding.Delegates;
using Satchel.BetterMenus;
using UnityEngine;
using UObject = UnityEngine.Object;
using PlaymakerFSM = PlayMakerFSM;

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

        public override string GetVersion() => "26.4.0.0";

        public override void Initialize(Dictionary<string, Dictionary<string, GameObject>> preloadedObjects)
        {
            Instance = this;
            HookAll();
        }

        private void HookAll()
        {
            ModHooks.SlashHitHook += OnSlashHitHook;
            ModHooks.AfterAttackHook += ResetCooldown;
            On.HeroController.StartMPDrain += SuppressMPDrain;
            On.HeroController.Awake += OnHeroAwake;
        }

        public void Unload()
        {
            ModHooks.SlashHitHook -= OnSlashHitHook;
            ModHooks.AfterAttackHook -= ResetCooldown;
            On.HeroController.StartMPDrain -= SuppressMPDrain;
            On.HeroController.Awake -= OnHeroAwake;
        }

        private void SuppressMPDrain(On.HeroController.orig_StartMPDrain orig, HeroController self, float speed) { }

        private void OnHeroAwake(On.HeroController.orig_Awake orig, HeroController self)
        {
            orig(self);
            GameManager.instance.StartCoroutine(SlowDrain());
        }

        private IEnumerator SlowDrain()
        {
            while (true)
            {
                float waitTime = Mathf.Lerp(6f, 1f, (GS.BloodlossRate - 1) / 9f);
                yield return new WaitForSeconds(waitTime);

                if (!GS.VampireEnabled) continue;

                if (!HeroController.instance.acceptingInput) continue;

                // this SUCKS i hate myself

                bool hasAliveEnemy = UnityEngine.Object.FindObjectsOfType<HealthManager>()
                    .Any(hm => !hm.isDead);

                bool hasVulnerableEnemy = UnityEngine.Object.FindObjectsOfType<HealthManager>()
                    .Any(hm => !hm.IsInvincible);

                if (!hasAliveEnemy || !hasVulnerableEnemy) continue;

                if (PlayerData.instance.health <= 1) continue;

                PlayerData.instance.health -= 1;

                GameObject healthObj = GameObject.Find("Health");
                if (healthObj != null)
                {
                    foreach (var fsm in healthObj.GetComponentsInChildren<PlaymakerFSM>(true)
                                                 .Where(f => f.FsmName == "health_display"))
                    {
                        fsm.SendEvent("HERO DAMAGED");
                    }
                }
            }
        }

        private void ResetCooldown(AttackDirection dir)
        {
            _vampireSwingHit = false;
        }

        private void UpdateMasks()
        {
            // livin' on a prayer

            GameObject healthObj = GameObject.Find("Health");

            if (healthObj == null && GameCameras.instance != null)
            {
                Log("Couldn't find Health HUD with basic search. Attempting deep search...");
                healthObj = GameCameras.instance.hudCanvas.GetComponentInChildren<PlaymakerFSM>(true)
                            .gameObject.scene.GetRootGameObjects()
                            .SelectMany(g => g.GetComponentsInChildren<PlaymakerFSM>(true))
                            .FirstOrDefault(f => f.FsmName == "Update Health")?.gameObject;
            }

            if (healthObj != null)
            {
                foreach (var fsm in healthObj.GetComponentsInChildren<PlaymakerFSM>(true)
                                 .Where(f => f.FsmName == "health_display"))
                {
                    fsm.SendEvent("HERO HEALED");
                }
            }
            else
            {
                Log("Couldn't find Health HUD even with deep search.");
            }
        }

        private void OnSlashHitHook(Collider2D enemyCollider, GameObject enemyGameObject)
        {
            // this is very poorly optimized BUT it works so i'm happy

            if (!GS.VampireEnabled) return;
            if (_vampireSwingHit) return;

            if (enemyCollider.gameObject.layer == 11)
            {
                HealthManager hm = enemyCollider.gameObject.GetComponent<HealthManager>()
                 ?? enemyCollider.gameObject.GetComponentInParent<HealthManager>();

                if (hm == null || hm.isDead) return;

                _vampireSwingHit = true;

                AudioClip carefreeSound = Resources.FindObjectsOfTypeAll<AudioClip>()
                   .FirstOrDefault(x => x.name.IndexOf("carefree", StringComparison.OrdinalIgnoreCase) >= 0);
                if (carefreeSound != null)
                {
                    HeroController.instance.GetComponent<AudioSource>().PlayOneShot(carefreeSound, 1f);
                }
                else
                {
                    Log("could not find carefree melody sfx. checking other sounds...");
                }

                if (PlayerData.instance.health < PlayerData.instance.maxHealth)
                {
                    PlayerData.instance.health += 1;
                }

                UpdateMasks();
            }
        }

        // i love satchel

        public bool ToggleButtonInsideMenu => false;

        public MenuScreen GetMenuScreen(MenuScreen modListMenu, ModToggleDelegates? toggleDelegates)
        {
            _menuRef ??= new Menu("VampireKnight", new Element[]
            {
                // blueprints.. like.. A BLUE SPY? A BLEU SPY'S IN TBHEZ BAZEZ I REPEATTTTTT

                Blueprints.HorizontalBoolOption(
                    name:         "Vampire Enabled",
                    description:  "Toggles the vampire mechanic on or off.",
                    applySetting: val => GS.VampireEnabled = val,
                    loadSetting:  ()  => GS.VampireEnabled
                ),

                Blueprints.IntInputField(
                    name:             "Bloodloss Rate",
                    _storeValue:      i => GS.BloodlossRate = Mathf.Clamp(i, 1, 10),
                    _loadValue:       () => GS.BloodlossRate,
                    _placeholder:     "Enter a value between 1 and 10",
                    _characterLimit:  2,
                    Id:               "BloodlossRateInput"
                ),

                new TextPanel("— Credits —"),
                new TextPanel("Author: ivory"),
                new TextPanel("Special Thanks: Charles Game Dev"),
                new TextPanel("Helped me figure out how to make this mod correctly through his scripts"),
            });

            return _menuRef.GetMenuScreen(modListMenu);
        }
    }
}