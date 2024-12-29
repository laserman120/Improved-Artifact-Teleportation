using SonsSdk;
using SonsSdk.Attributes;
using UnityEngine;
using Sons.Gameplay;
using static ImprovedArtifactTeleportation.ImprovedArtifactTeleportation;
using RedLoader;
using Sons.Items;
using Sons.Gameplay.Artifact;
using static RedLoader.RLog;
using HarmonyLib;
using Sons.Animation.PlayerControl;
using Sons.Gameplay.GPS;
using TheForest.Utils;
using UnityEngine.InputSystem;
using UnityEngine.Animations;
using UnityEngine.InputSystem.XR;
using System.Collections;
using UnityEngine.SceneManagement;

namespace ImprovedArtifactTeleportation;

public class ImprovedArtifactTeleportation : SonsMod, IOnGameActivatedReceiver
{
    private static bool _firstRun = true;
    public static ArtifactItemController _artifactItemController;
    public static TeleportationArtifactAbility _artifactTeleportAbility;
    public static ArtifactPortal _currentSetPortal;

    public static int _fuelCost = 34;
    public static float _laserDisableRange = 80f;
    public static Vector3 MapPosition;
    public static bool IsPlayerBusy = false;

    public static bool ArtifactEquippedAndInPortalMode = false;

    public ImprovedArtifactTeleportation()
    {

        // Uncomment any of these if you need a method to run on a specific update loop.
        //OnUpdateCallback = MyUpdateMethod;
        //OnLateUpdateCallback = MyLateUpdateMethod;
        //OnFixedUpdateCallback = MyFixedUpdateMethod;
        //OnGUICallback = MyGUIMethod;

        // Uncomment this to automatically apply harmony patches in your assembly.
        HarmonyPatchAll = true;
    }

    protected override void OnInitializeMod()
    {
        // Do your early mod initialization which doesn't involve game or sdk references here
        Config.Init();
    }

    protected override void OnSdkInitialized()
    {
        // Do your mod initialization which involves game or sdk references here
        // This is for stuff like UI creation, event registration etc.
        ImprovedArtifactTeleportationUi.Create();

        // Add in-game settings ui for your mod.
        // SettingsRegistry.CreateSettings(this, null, typeof(Config));
    }

    protected override void OnGameStart()
    {
        // This is called once the player spawns in the world and gains control.
    }

    public void OnGameActivated()
    {
        if (_firstRun)
        {
            //artifact help id 707
            ItemTools.GetHeldPrefab(707).gameObject.AddComponent<ArtifactComponentDataCollector>();
            ConstructionTools.GetRecipe(88)._builtPrefab.AddComponent<TeleporterListComponent>();
            ConstructionTools.GetRecipe(88)._builtPrefab.AddComponent<PortalParticleEffectHelper>();
            _firstRun = false;
        }
    }

    private static void SwitchPortal()
    {
        if (!_currentSetPortal) { ClearBusyDelay().RunCoro(); return; };
        //If player was interrupted, setting the value to false, return out, switch canceled
        if (!IsPlayerBusy) { ClearBusyDelay().RunCoro(); return; }
        var CurrentFuel = _artifactItemController._fuel._CurrentVolume_k__BackingField;

        //Do not run if the fuel is out
        if (CurrentFuel < _fuelCost) { ClearBusyDelay().RunCoro(); return; }

        //Find the current Portal
        var allTeleporters = TeleporterListComponent.instances
                                                                .Where(instance => instance != null)  // Filter out null instances
                                                                .Select(instance => instance.gameObject)
                                                                .ToList();
        int currentPortalIndex = allTeleporters.FindIndex(portal => portal.transform.position == _currentSetPortal.transform.position);

        if (currentPortalIndex == -1)
        {
            ClearBusyDelay().RunCoro(); return;
        }

        //Remove Fuel From the Artifact
        _artifactItemController._fuel._CurrentVolume_k__BackingField = CurrentFuel - _fuelCost;

        // Calculate the index of the next portal (looping back to the first if at the end)
        int nextPortalIndex = (currentPortalIndex + 1) % allTeleporters.Count;

        // Switch to the next portal
        _artifactTeleportAbility._portal = allTeleporters[nextPortalIndex].GetComponent<ArtifactPortal>();

        //Enable or disable the correct portal
        _currentSetPortal.Activate(false); //old portal
        _artifactTeleportAbility._portal.Activate(true); //new portal

        //trigger lightning at the newly activated portal
        _artifactItemController.TriggerLightning(allTeleporters[nextPortalIndex].transform.position);

        //Set Current Portal to the new one
        _currentSetPortal = ImprovedArtifactTeleportation._artifactTeleportAbility._portal;
        //reset busy bool
        ClearBusyDelay().RunCoro();
        
    }

    [HarmonyPatch(typeof(ArtifactPortal), "Activate")]
    private static class ArtifactPortalPatch
    {
        private static void Postfix(ArtifactPortal __instance, bool activate)
        {
            if (activate)
            {
                _currentSetPortal = __instance;
            }
            else
            {
                _currentSetPortal = null;
            }
        }
    }
    //Teleport
    [HarmonyPatch(typeof(TeleportationArtifactAbility), "OnEnable")]
    private static class TeleportArtifactAbilityOnEnablePatch
    {
        private static void Postfix(TeleportationArtifactAbility __instance)
        {
            ImprovedArtifactTeleportation._artifactTeleportAbility = __instance;
            _currentSetPortal = __instance._portal;
            ArtifactEquippedAndInPortalMode = true;
            IsPlayerBusy = false;
        }
    }

    [HarmonyPatch(typeof(TeleportationArtifactAbility), "OnDisable")]
    private static class TeleportArtifactAbilityOnDisablePatch
    {
        private static void Postfix(TeleportationArtifactAbility __instance)
        {
            ImprovedArtifactTeleportation._artifactTeleportAbility = null;
            _currentSetPortal = null;
            ArtifactEquippedAndInPortalMode = false;
            IsPlayerBusy = false;
        }
    }

    [HarmonyPatch(typeof(ArtifactItemController), "OnSecondaryAction")]
    private static class ArtifactItemController_OnSecondaryAction_Patch
    {
        private static void Prefix(InputAction.CallbackContext context)
        {
            if (context.performed)
            {

                if (_artifactItemController._targetingBeam.active) { return; }

                if(_artifactItemController._isReloading) { return; }
                if (_artifactItemController._isReloading) { return; }
                
                //if player is busy, return out
                if (IsPlayerBusy) { return; } else { IsPlayerBusy = true; }
                // Get the Animator component
                Animator playerAnimator = ImprovedArtifactTeleportation._artifactItemController._playerAnimator;

                if (playerAnimator != null)
                {

                    playerAnimator.Play(Sons.Animation.AnimationHashes.PlayerAArtifactActivateStructureHash);
                    RunMainCode().RunCoro();
                }
                else
                {
                    RLog.Msg("Animator not found on the artifact item!");
                }
                    
            }
                
        }
    }

    private static IEnumerator RunMainCode()
    {
        yield return new WaitForSeconds(0.8f);
        SwitchPortal();
    }

    private static IEnumerator ClearBusyDelay()
    {
        yield return new WaitForSeconds(0.5f);
        IsPlayerBusy = false;
    }

}

[RegisterTypeInIl2Cpp]
public class PortalParticleEffectHelper : MonoBehaviour
{
    public GameObject clonedCaveEffects;
    public GameObject clonedLightColumn;
    public bool IsParticleEffectActive = false;
    public ArtifactPortal Portal;
    public TeleporterListComponent TeleporterListComponent;
    public PlayerDetectionTrigger PlayerDetectionTrigger;

    void OnEnable()
    {
        Portal = transform.Find("ArtifactPortal").GetComponent<ArtifactPortal>();
        if (Portal == null) { RLog.Msg("Failed to find Component ArtifactPortal!"); }

        TeleporterListComponent = GetComponent<TeleporterListComponent>();
        PlayerDetectionTrigger = TeleporterListComponent.playerDetectionTrigger;
        if (PlayerDetectionTrigger == null) { RLog.Msg("Failed to find Component PlayerDetectionTrigger!"); }
    }
    void Start()
    {
        Scene scene = SceneManager.GetSceneByName("SonsCaveH");
        if (scene.IsValid())
        {
            GameObject[] rootObjects = scene.GetRootGameObjects();

            foreach (GameObject rootObject in rootObjects)
            {
                if (rootObject.name == "CaveHExternal")
                {
                    Transform caveEffectsTransform = rootObject.transform.Find("CaveHExternalOpened")
                                                                         .Find("ArtifactActiveExternal")
                                                                         .Find("DAGGroup")
                                                                         .Find("CaveHEffectsExternal");
                    if (caveEffectsTransform != null)
                    {
                        clonedCaveEffects = Instantiate(caveEffectsTransform.gameObject, transform);
                        clonedCaveEffects.name = "ClonedCaveEffects";
                        clonedCaveEffects.transform.localPosition = Vector3.zero;
                    }
                    else
                    {
                        RLog.Msg("CaveHEffectsExternal not found within the hierarchy!");
                    }
                    // Find the LightColumnCenter object
                    Transform lightColumnTransform = rootObject.transform.Find("CaveHExternalOpened")
                                                              ?.Find("ArtifactActiveExternal")
                                                              ?.Find("LightColumns")
                                                              ?.Find("LightColumnCenter");
                    if (lightColumnTransform != null)
                    {
                        clonedLightColumn = Instantiate(lightColumnTransform.gameObject, transform);
                        clonedLightColumn.transform.localPosition = Vector3.zero;
                        ShrinkColumn(clonedLightColumn);
                    }
                    else
                    {
                        RLog.Msg("LightColumnCenter not found within the hierarchy!");
                    }
                }
            }
        }
        else
        {
            RLog.Msg("Scene SonsCaveH not found!");
        }
        ToggleParticleEffect(false);
    }
    void Update()
    {
        if (ArtifactEquippedAndInPortalMode && _currentSetPortal != null && IsPortalActive() && !PlayerDetectionTrigger.isPlayerNearby)
        {
            if (!IsParticleEffectActive)
            {
                ToggleParticleEffect(true);
            }
        }
        else if (IsParticleEffectActive) // Deactivate if any condition is false
        {
            ToggleParticleEffect(false);
        }
    }

    public void ToggleParticleEffect(bool enable)
    {
        if (clonedCaveEffects != null)
        {
            ParticleSystem ps = clonedCaveEffects.GetComponentInChildren<ParticleSystem>();
            if (ps != null)
            {
                if (enable)
                {
                    ps.Play();
                }
                else
                {
                    ps.Stop();
                }
            }

            if (clonedLightColumn != null)
            {
                if (enable)
                {
                    GrowColumn(clonedLightColumn);
                }
                else
                {
                    ShrinkColumn(clonedLightColumn);
                }
            }

            IsParticleEffectActive = enable;
        }
    }

    private Coroutines.CoroutineToken currentCoroutine; // Store the currently running coroutine

    public void GrowColumn(GameObject clonedLightColumn)
    {
        // Stop the currently running coroutine (if any)
        if (currentCoroutine != null)
        {
            currentCoroutine.Stop();
        }

        // Start the GrowCoroutine
        currentCoroutine = GrowCoroutine(clonedLightColumn).RunCoro();
    }

    public void ShrinkColumn(GameObject clonedLightColumn)
    {
        // Stop the currently running coroutine (if any)
        if (currentCoroutine != null)
        {
            currentCoroutine.Stop();
        }

        // Start the ShrinkCoroutine
        currentCoroutine = ShrinkCoroutine(clonedLightColumn).RunCoro();
    }

    private IEnumerator GrowCoroutine(GameObject clonedLightColumn)
    {
        Transform lightTransform = clonedLightColumn.transform;
        float targetScale = 1f; // Target scale for growing
        float speed = 2f;      // Speed of the scaling animation (units per second)

        clonedLightColumn.SetActive(true);

        while (lightTransform.localScale.y < targetScale)
        {
            // Calculate the amount to scale this frame
            float scaleIncrease = speed * Time.deltaTime;

            // Ensure we don't overshoot the target scale
            scaleIncrease = Mathf.Min(scaleIncrease, targetScale - lightTransform.localScale.y);

            lightTransform.localScale = new Vector3(
                lightTransform.localScale.x,
                lightTransform.localScale.y + scaleIncrease,
                lightTransform.localScale.z
            );

            yield return null;
        }

        // Ensure the final scale is exactly 1
        lightTransform.localScale = new Vector3(
            lightTransform.localScale.x,
            targetScale,
            lightTransform.localScale.z
        ); 
    }

    private IEnumerator ShrinkCoroutine(GameObject clonedLightColumn)
    {
        Transform lightTransform = clonedLightColumn.transform;
        float targetScale = 0f; // Target scale for shrinking
        float speed = 2f;      // Speed of the scaling animation (units per second)

        while (lightTransform.localScale.y > targetScale)
        {
            // Calculate the amount to scale this frame
            float scaleDecrease = speed * Time.deltaTime;

            // Ensure we don't overshoot the target scale
            scaleDecrease = Mathf.Min(scaleDecrease, lightTransform.localScale.y - targetScale);

            lightTransform.localScale = new Vector3(
                lightTransform.localScale.x,
                lightTransform.localScale.y - scaleDecrease,
                lightTransform.localScale.z
            );

            yield return null;
        }

        // Ensure the final scale is exactly 0
        lightTransform.localScale = new Vector3(
            lightTransform.localScale.x,
            targetScale,
            lightTransform.localScale.z
        );

        clonedLightColumn.SetActive(false);
    }

    public bool ParticleEffectActive()
    {
        return IsParticleEffectActive;
    }

    public bool IsPortalActive()
    {
        return (Portal == _currentSetPortal);
    }
}

[RegisterTypeInIl2Cpp]
public class PlayerDetectionTrigger : MonoBehaviour
{
    private SphereCollider proximityCollider;
    public float proximityRadius = _laserDisableRange;
    public bool isPlayerNearby = false;

    void Start()
    {
        proximityCollider = gameObject.AddComponent<SphereCollider>();

        proximityCollider.radius = proximityRadius;
        proximityCollider.isTrigger = true; 
    }

    private void OnTriggerEnter(Collider other)
    {
        Transform playerTransform = other.transform;

        if (playerTransform.name.Contains("LocalPlayer"))
        {
            isPlayerNearby = true;
        }
    }

    private void OnTriggerExit(Collider other)
    {
        Transform playerTransform = other.transform;

        if (playerTransform.name.Contains("LocalPlayer"))
        {
            isPlayerNearby = false;
        }
    }
}

[RegisterTypeInIl2Cpp]
public class TeleporterListComponent : MonoBehaviour
{
    public static List<ArtifactPortal> instances = new List<ArtifactPortal>();
    public PlayerDetectionTrigger playerDetectionTrigger;
    private GameObject triggerObject;

    void OnEnable()
    {
        ArtifactPortal portal = GetComponentInChildren<ArtifactPortal>();

        if (portal != null)
        {
            instances.Add(portal);
        }
        else
        {
            RLog.Msg("ArtifactPortal not found on this TeleporterStructure!");
        }

        // SetupPlayerDetection trigger
        triggerObject = new GameObject("PortalPlayerDetectionTrigger");

        triggerObject.transform.position = transform.position;

        playerDetectionTrigger = triggerObject.AddComponent<PlayerDetectionTrigger>();
    }

    void OnDisable()
    {
        ArtifactPortal portal = GetComponentInChildren<ArtifactPortal>();

        if (portal != null)
        {
            instances.Remove(portal);
        }

        GameObject.Destroy(triggerObject);
    }

    void OnDestroy()
    {
        OnDisable();
    }

    public void LogAllTeleporters()
    {
        foreach (var teleporter in instances)
        {
            RLog.Msg(teleporter.gameObject.name + "     " + teleporter.transform.position);
        }
    }
}
//Data Collector for the ArtifactItemController
[RegisterTypeInIl2Cpp]
public class ArtifactComponentDataCollector : MonoBehaviour
{
    void OnEnable()
    {
        GameObject ArtifactGameObject = gameObject;
        ImprovedArtifactTeleportation._artifactItemController = ArtifactGameObject.GetComponent<ArtifactItemController>();
    }

    void OnDisable ()
    {
        ImprovedArtifactTeleportation._artifactItemController = null;
    }
}