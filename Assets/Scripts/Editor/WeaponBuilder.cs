using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// Tools > AutoMars > Weapon > Build Weapons: makes one WeaponDef per gun model (Resources/Weapons), a pickup
/// prefab for each (Assets/Prefabs/Weapons), puts PlayerWeapon on the player with the pistol, and adds the
/// weapon tile to the mobile HUD. Safe to re-run: a WeaponDef that already exists keeps its numbers
/// (only its model, icon and effects are re-linked); delete the asset to get the defaults back.
public static class WeaponBuilder
{
    const string DefFolder = "Assets/Resources/Weapons";
    const string PrefabFolder = "Assets/Prefabs/Weapons";
    const string Guns = "Assets/3DBSciFiTool/";
    const string Fx = "Assets/Epic Toon FX/Prefabs/Combat/";
    const string FxLoot = "Assets/Epic Toon FX/Prefabs/Interactive/";
    const string Hovl = "Assets/Hovl Studio/";
    const string HovlProj = Hovl + "AAA Projectiles Vol 1/Prefabs/";

    class Spec
    {
        public string file, name, model, icon;
        public Color color;
        public float damage, range, interval, reload, spread, speed, homing, radius = 0.15f, splashRadius, splashDamage, knock;
        public int magazine, pellets = 1, pierce;
        public float recoilAngle, recoilBack, recoilTime, shotShake, hitShake, killShake, flash;
        public bool casings;
        public string projectile, muzzle, impact;
        public float projectileScale, muzzleScale, impactScale;
        public float muzzleY = float.NaN;   // override for models whose barrel isn't their front-most part
        public string variantOf;            // file of the weapon this is a second take on (its test pickup lies next to that one)
        public bool beam, rangeArc;
        public bool hovl;                   // projectile / muzzle / impact are names from Hovl's AAA Projectiles (Projectile N, Flash N, Hit N)
        public float side;                  // where a variant's test pickup lies, in metres east of its original
        public string beamFx;
        public float damageMax, rampTime, beamTick = 0.2f, beamScale = 0.5f, tracerLength;
    }

    static readonly Spec[] Specs =
    {
        // the starter: six clean shots
        new Spec { file = "Weapon_Pistol", name = "PISTOL", model = "Guns01/Cosmic_Retro_Blaster_3_4.fbx", icon = "Guns01/Icons_UI/SideView/SideView_Blaster_3_4.png",
            color = new Color(1f, 0.78f, 0.3f), damage = 4f, range = 6f, magazine = 6, interval = 0.26f, reload = 1.1f,
            speed = 30f, homing = 120f, knock = 0.3f,
            recoilAngle = 24f, recoilBack = 0.1f, recoilTime = 0.16f, shotShake = 0.05f, hitShake = 0.08f, killShake = 0.3f, flash = 5f, casings = true,
            projectile = "Missiles/Bullet/BulletMeshSmallFire", muzzle = "Muzzleflash/BulletMuzzle/BulletMuzzleFire", impact = "Explosions/BulletExplosion/BulletExplosionFire",
            projectileScale = 0.7f, muzzleScale = 0.55f, impactScale = 0.55f },

        // a stream of small bullets, a little wild
        new Spec { file = "Weapon_Rapid", name = "RAPID", model = "Guns01/Cosmic_Retro_Blaster_3_6.fbx", icon = "Guns01/Icons_UI/SideView/SideView_Blaster_3_6.png",
            color = new Color(0.45f, 1f, 0.55f), damage = 2f, range = 5.5f, magazine = 20, interval = 0.085f, reload = 1.5f, spread = 7f,
            speed = 34f, homing = 60f, knock = 0.1f,
            recoilAngle = 9f, recoilBack = 0.05f, recoilTime = 0.08f, shotShake = 0.025f, hitShake = 0.04f, killShake = 0.25f, flash = 3f, casings = true,
            projectile = "Missiles/Bullet/BulletMeshSmallGreen", muzzle = "Muzzleflash/BulletMuzzle/BulletMuzzleGreen", impact = "Explosions/BulletExplosion/BulletExplosionGreen",
            projectileScale = 0.5f, muzzleScale = 0.4f, impactScale = 0.4f },

        // slow and heavy: big bullet, big kick, goes through one enemy into the next
        new Spec { file = "Weapon_Revolver", name = "REVOLVER", model = "Guns01/Cosmic_Retro_Blaster_3_9.fbx", icon = "Guns01/Icons_UI/SideView/SideView_Blaster_3_9.png",
            color = new Color(1f, 0.6f, 0.2f), damage = 10f, range = 6.5f, magazine = 6, interval = 0.55f, reload = 1.6f,
            speed = 40f, homing = 90f, radius = 0.2f, pierce = 1, knock = 0.9f,
            recoilAngle = 42f, recoilBack = 0.16f, recoilTime = 0.28f, shotShake = 0.12f, hitShake = 0.14f, killShake = 0.4f, flash = 8f, casings = false,
            projectile = "Missiles/FatBullet/BulletMeshFatFire", muzzle = "Muzzleflash/- Misc/GunFire/GunFireYellow", impact = "Explosions/BulletFatExplosion/BulletFatExplosionFire",
            projectileScale = 0.95f, muzzleScale = 0.75f, impactScale = 0.8f },

        // two barrels: a fan of pellets, strong up close, short range
        new Spec { file = "Weapon_Scatter", name = "SCATTER", model = "Guns02/Cosmic_Retro_Blaster_1.fbx", icon = "Guns02/Icons_UI/SideView/SideView_Blaster_1_1.png",
            color = new Color(1f, 0.45f, 0.75f), damage = 2f, pellets = 6, spread = 30f, range = 4.5f, magazine = 2, interval = 0.4f, reload = 1.3f,
            speed = 26f, homing = 0f, knock = 0.25f,
            recoilAngle = 36f, recoilBack = 0.18f, recoilTime = 0.3f, shotShake = 0.14f, hitShake = 0.05f, killShake = 0.35f, flash = 9f, casings = true,
            projectile = "Missiles/Bullet/BulletMeshSmallPink", muzzle = "Muzzleflash/BulletFatMuzzle/BulletFatMuzzlePink", impact = "Explosions/BulletExplosion/BulletExplosionPink",
            projectileScale = 0.5f, muzzleScale = 0.8f, impactScale = 0.4f },

        // laser bolts that go through every enemy in the line
        new Spec { file = "Weapon_RayGun", name = "RAY GUN", model = "Guns02/Cosmic_Retro_Blaster_10.fbx", icon = "Guns02/Icons_UI/SideView/SideView_Blaster_1_10.png",
            color = new Color(1f, 0.3f, 0.25f), damage = 3f, range = 6.5f, magazine = 10, interval = 0.2f, reload = 1.4f,
            speed = 45f, homing = 60f, pierce = 99, knock = 0.12f,
            recoilAngle = 8f, recoilBack = 0.04f, recoilTime = 0.1f, shotShake = 0.03f, hitShake = 0.05f, killShake = 0.3f, flash = 4f, casings = false,
            projectile = "Missiles/Laser/LaserMeshMissileRed", muzzle = "Muzzleflash/LaserMuzzle/LaserMuzzleRed", impact = "Explosions/LaserExplosion/LaserExplosionRed",
            projectileScale = 0.8f, muzzleScale = 0.5f, impactScale = 0.5f },

        // a slow energy ball that follows its target and explodes
        new Spec { file = "Weapon_Nova", name = "NOVA", model = "Guns02/Cosmic_Retro_Blaster_11.fbx", icon = "Guns02/Icons_UI/SideView/SideView_Blaster_1_11.png",
            color = new Color(0.4f, 0.75f, 1f), damage = 10f, splashRadius = 2.4f, splashDamage = 6f, range = 6f, magazine = 3, interval = 0.8f, reload = 2f,
            speed = 13f, homing = 140f, radius = 0.3f, knock = 1f,
            recoilAngle = 30f, recoilBack = 0.2f, recoilTime = 0.35f, shotShake = 0.1f, hitShake = 0.2f, killShake = 0.45f, flash = 7f, casings = false,
            projectile = "Missiles/NovaSmall/NovaMissileSmallBlue", muzzle = "Muzzleflash/EnergyNovaSmallMuzzle/EnergyNovaMuzzleSmallBlue", impact = "Explosions/NovaExplosion/ExplosionNovaBlue",
            projectileScale = 1.6f, muzzleScale = 0.8f, impactScale = 1f, muzzleY = 0.2f },

        // ---- second takes, to compare with the originals (same models)

        // SCATTER B: short reach and a wide fan, and every pellet flies on through everything in the fan,
        // so a tight group all gets hit. Shoves hard. Bright tracers and a fan on the ground show the reach.
        new Spec { file = "Weapon_ScatterB", variantOf = "Weapon_Scatter", side = -1.9f, name = "SCATTER B", model = "Guns02/Cosmic_Retro_Blaster_1.fbx", icon = "Guns02/Icons_UI/SideView/SideView_Blaster_1_1.png",
            color = new Color(1f, 0.55f, 0.2f), damage = 2f, pellets = 8, spread = 55f, range = 3.6f, magazine = 2, interval = 0.45f, reload = 1.3f,
            speed = 30f, homing = 0f, pierce = 99, knock = 0.8f,
            recoilAngle = 42f, recoilBack = 0.2f, recoilTime = 0.32f, shotShake = 0.18f, hitShake = 0.06f, killShake = 0.35f, flash = 10f, casings = true,
            projectile = "Missiles/Bullet/BulletMeshSmallFire", muzzle = "Muzzleflash/- Misc/GunFire/GunFireYellow", impact = "Explosions/BulletExplosion/BulletExplosionFire",
            projectileScale = 0.5f, muzzleScale = 1f, impactScale = 0.4f, tracerLength = 1.8f, rangeArc = true },

        // RAY GUN B: a beam on one enemy. Small damage at first, more and more the longer it stays on the same
        // enemy; a new enemy starts over. The magazine is beam time (30 ticks = 6 s).
        new Spec { file = "Weapon_RayGunB", variantOf = "Weapon_RayGun", side = -1.9f, name = "RAY GUN B", model = "Guns02/Cosmic_Retro_Blaster_10.fbx", icon = "Guns02/Icons_UI/SideView/SideView_Blaster_1_10.png",
            color = new Color(1f, 0.3f, 0.25f), beam = true, beamFx = "3D Lasers Pack/Prefabs/Laser beam 4 red", beamScale = 0.9f, beamTick = 0.2f,
            damage = 1f, damageMax = 4f, rampTime = 2.4f, range = 6f, magazine = 30, interval = 0.2f, reload = 1.6f,
            speed = 45f, homing = 0f, knock = 0.05f,
            recoilAngle = 5f, recoilBack = 0.03f, recoilTime = 0.12f, shotShake = 0f, hitShake = 0.03f, killShake = 0.3f, flash = 3f, casings = false,
            projectile = "Missiles/Laser/LaserMeshMissileRed", muzzle = "Muzzleflash/LaserMuzzle/LaserMuzzleRed", impact = "Explosions/LaserExplosion/LaserExplosionRed",
            projectileScale = 0.8f, muzzleScale = 0.5f, impactScale = 0.5f, rangeArc = true },

        // ---- same weapons with the projectile, muzzle flash and hit from Hovl's AAA Projectiles (RPG VFX Bundle),
        //      to compare the looks. The numbers are the originals'.

        new Spec { file = "Weapon_PistolH", variantOf = "Weapon_Pistol", side = 2.6f, hovl = true, name = "PISTOL H", model = "Guns01/Cosmic_Retro_Blaster_3_4.fbx", icon = "Guns01/Icons_UI/SideView/SideView_Blaster_3_4.png",
            color = new Color(1f, 0.85f, 0.3f), damage = 4f, range = 6f, magazine = 6, interval = 0.26f, reload = 1.1f,
            speed = 30f, homing = 120f, knock = 0.3f,
            recoilAngle = 24f, recoilBack = 0.1f, recoilTime = 0.16f, shotShake = 0.05f, hitShake = 0.08f, killShake = 0.3f, flash = 5f, casings = true,
            projectile = "Projectile 4 yellow arrow", muzzle = "Flash 4 yellow arrow", impact = "Hit 4 yellow arrow",
            projectileScale = 0.6f, muzzleScale = 0.6f, impactScale = 0.6f },

        new Spec { file = "Weapon_RapidH", variantOf = "Weapon_Rapid", side = 2.6f, hovl = true, name = "RAPID H", model = "Guns01/Cosmic_Retro_Blaster_3_6.fbx", icon = "Guns01/Icons_UI/SideView/SideView_Blaster_3_6.png",
            color = new Color(0.4f, 0.75f, 1f), damage = 2f, range = 5.5f, magazine = 20, interval = 0.085f, reload = 1.5f, spread = 7f,
            speed = 34f, homing = 60f, knock = 0.1f,
            recoilAngle = 9f, recoilBack = 0.05f, recoilTime = 0.08f, shotShake = 0.025f, hitShake = 0.04f, killShake = 0.25f, flash = 3f, casings = true,
            projectile = "Projectile 14 blue rapid", muzzle = "Flash 14 blue rapid", impact = "Hit 14 blue rapid",
            projectileScale = 0.5f, muzzleScale = 0.5f, impactScale = 0.5f },

        new Spec { file = "Weapon_RevolverH", variantOf = "Weapon_Revolver", side = 2.6f, hovl = true, name = "REVOLVER H", model = "Guns01/Cosmic_Retro_Blaster_3_9.fbx", icon = "Guns01/Icons_UI/SideView/SideView_Blaster_3_9.png",
            color = new Color(1f, 0.55f, 0.15f), damage = 10f, range = 6.5f, magazine = 6, interval = 0.55f, reload = 1.6f,
            speed = 40f, homing = 90f, radius = 0.2f, pierce = 1, knock = 0.9f,
            recoilAngle = 42f, recoilBack = 0.16f, recoilTime = 0.28f, shotShake = 0.12f, hitShake = 0.14f, killShake = 0.4f, flash = 8f, casings = false,
            projectile = "Projectile 16 fire", muzzle = "Flash 16 fire", impact = "Hit 16 fire",
            projectileScale = 0.7f, muzzleScale = 0.7f, impactScale = 0.8f },

        new Spec { file = "Weapon_RayGunH", variantOf = "Weapon_RayGun", side = 2.6f, hovl = true, name = "RAY GUN H", model = "Guns02/Cosmic_Retro_Blaster_10.fbx", icon = "Guns02/Icons_UI/SideView/SideView_Blaster_1_10.png",
            color = new Color(1f, 0.3f, 0.25f), damage = 3f, range = 6.5f, magazine = 10, interval = 0.2f, reload = 1.4f,
            speed = 45f, homing = 60f, pierce = 99, knock = 0.12f,
            recoilAngle = 8f, recoilBack = 0.04f, recoilTime = 0.1f, shotShake = 0.03f, hitShake = 0.05f, killShake = 0.3f, flash = 4f, casings = false,
            projectile = "Projectile 13 red laser", muzzle = "Flash 13 red laser", impact = "Hit 13 red laser",
            projectileScale = 0.6f, muzzleScale = 0.6f, impactScale = 0.6f },

        new Spec { file = "Weapon_NovaH", variantOf = "Weapon_Nova", side = 2.6f, hovl = true, name = "NOVA H", model = "Guns02/Cosmic_Retro_Blaster_11.fbx", icon = "Guns02/Icons_UI/SideView/SideView_Blaster_1_11.png",
            color = new Color(0.75f, 0.45f, 1f), damage = 10f, splashRadius = 2.4f, splashDamage = 6f, range = 6f, magazine = 3, interval = 0.8f, reload = 2f,
            speed = 13f, homing = 140f, radius = 0.3f, knock = 1f,
            recoilAngle = 30f, recoilBack = 0.2f, recoilTime = 0.35f, shotShake = 0.1f, hitShake = 0.2f, killShake = 0.45f, flash = 7f, casings = false,
            projectile = "Projectile 17 nova violet", muzzle = "Flash 17 nova violet", impact = "Hit 17 nova violet",
            projectileScale = 0.9f, muzzleScale = 0.8f, impactScale = 0.9f, muzzleY = 0.2f },
    };

    [MenuItem("Tools/AutoMars/Weapon/Build Weapons (definitions, pickup prefabs, player, HUD tile)")]
    public static void Build()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
        if (!AssetDatabase.IsValidFolder(DefFolder)) AssetDatabase.CreateFolder("Assets/Resources", "Weapons");
        if (!AssetDatabase.IsValidFolder("Assets/Prefabs")) AssetDatabase.CreateFolder("Assets", "Prefabs");
        if (!AssetDatabase.IsValidFolder(PrefabFolder)) AssetDatabase.CreateFolder("Assets/Prefabs", "Weapons");

        var defs = new WeaponDef[Specs.Length];
        for (int i = 0; i < Specs.Length; i++) defs[i] = MakeDef(Specs[i]);
        AssetDatabase.SaveAssets();
        foreach (var d in defs) MakePickupPrefab(d);

        // ---- player
        var tools = Object.FindFirstObjectByType<PlayerTools>(FindObjectsInactive.Include);
        if (tools == null) { Debug.LogWarning("Weapons: no PlayerTools in this scene, only the assets were made."); return; }
        GameObjectUtility.RemoveMonoBehavioursWithMissingScript(tools.gameObject);   // the old PlayerPistol
        var fore = tools.rightForearm;
        if (fore != null)
        {
            var oldModel = fore.Find("Pistol");   // the primitive pistol
            if (oldModel != null) Object.DestroyImmediate(oldModel.gameObject);
        }
        var pw = tools.GetComponent<PlayerWeapon>();
        if (pw == null) pw = Undo.AddComponent<PlayerWeapon>(tools.gameObject);
        if (pw.weapon == null) pw.weapon = defs[0];
        pw.rightUpperArm = tools.rightUpperArm;
        pw.rightForearm = fore;
        pw.hand = fore != null ? fore.Find(fore.name + "_end") : null;
        if (pw.hand == null) pw.hand = fore;
        if (pw.pickupGlowFx == null) pw.pickupGlowFx = AssetDatabase.LoadAssetAtPath<GameObject>(FxLoot + "Loot/ItemSparkle/ItemSparkleYellow.prefab");
        if (pw.pickupTakeFx == null) pw.pickupTakeFx = AssetDatabase.LoadAssetAtPath<GameObject>(FxLoot + "Powerups/PowerupActivate/PowerupActivateYellow.prefab");
        tools.weapon = pw;
        EditorUtility.SetDirty(pw);
        EditorUtility.SetDirty(tools);

        MobileUIBuilder.AddWeaponSlot();
        MobileUIBuilder.AddToolBar();
        EditorSceneManager.MarkSceneDirty(tools.gameObject.scene);
        Debug.Log("Weapons built: " + defs.Length + " definitions in " + DefFolder + ", pickup prefabs in " + PrefabFolder + ", PlayerWeapon on " + tools.name + ".");
    }

    /// Puts one pickup of every weapon the player isn't holding in a row next to him (to try them out).
    [MenuItem("Tools/AutoMars/Weapon/Place Test Pickups Next To The Player")]
    public static void PlaceTestPickups()
    {
        var tools = Object.FindFirstObjectByType<PlayerTools>(FindObjectsInactive.Include);
        var pw = tools != null ? tools.GetComponent<PlayerWeapon>() : null;
        if (pw == null) { Debug.LogError("Weapons: build the weapons first"); return; }
        var old = GameObject.Find("Weapon Pickups");
        if (old != null) Object.DestroyImmediate(old);
        var parent = new GameObject("Weapon Pickups").transform;
        Vector3 origin = tools.transform.position;
        var rowOf = new System.Collections.Generic.Dictionary<string, int>();
        int n = 0, rows = 0;
        foreach (var s in Specs)
        {
            // a second take lies next to the weapon it is a take on, so the two can be compared
            int row;
            float side = 0f;
            if (s.variantOf != null && rowOf.TryGetValue(s.variantOf, out row)) side = s.side;
            else { row = rows++; rowOf[s.file] = row; }
            var def = AssetDatabase.LoadAssetAtPath<WeaponDef>(DefFolder + "/" + s.file + ".asset");
            if (def == null || def == pw.weapon) continue;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder + "/Pickup_" + s.file.Substring(7) + ".prefab");
            if (prefab == null) continue;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            Vector3 p = origin + new Vector3(3.5f + side, 0f, 4f - (row - 1) * 2.4f);
            p.y = GroundUtil.Height(p, origin.y);
            go.transform.position = p;
            n++;
        }
        EditorSceneManager.MarkSceneDirty(tools.gameObject.scene);
        Debug.Log("Weapons: " + n + " test pickups placed under 'Weapon Pickups'.");
    }

    static WeaponDef MakeDef(Spec s)
    {
        string path = DefFolder + "/" + s.file + ".asset";
        var d = AssetDatabase.LoadAssetAtPath<WeaponDef>(path);
        bool fresh = d == null;
        if (fresh)
        {
            d = ScriptableObject.CreateInstance<WeaponDef>();
            AssetDatabase.CreateAsset(d, path);
            d.displayName = s.name;
            d.color = s.color;
            d.damage = s.damage; d.range = s.range; d.magazine = s.magazine; d.fireInterval = s.interval; d.reloadTime = s.reload;
            d.pellets = s.pellets; d.spread = s.spread;
            d.speed = s.speed; d.homing = s.homing; d.radius = s.radius; d.pierce = s.pierce;
            d.splashRadius = s.splashRadius; d.splashDamage = s.splashDamage; d.knockback = s.knock;
            d.recoilAngle = s.recoilAngle; d.recoilBack = s.recoilBack; d.recoilTime = s.recoilTime;
            d.shotShake = s.shotShake; d.hitShake = s.hitShake; d.killShake = s.killShake; d.flashLight = s.flash; d.casings = s.casings;
            d.projectileScale = s.projectileScale; d.muzzleScale = s.muzzleScale; d.impactScale = s.impactScale;
            d.modelScale = 1.4f;
            d.mode = s.beam ? WeaponMode.Beam : WeaponMode.Projectile;
            d.beamScale = s.beamScale; d.beamTick = s.beamTick; d.damageMax = s.damageMax; d.rampTime = s.rampTime;
            d.tracerLength = s.tracerLength; d.rangeArc = s.rangeArc;
        }
        d.model = AssetDatabase.LoadAssetAtPath<GameObject>(Guns + s.model);
        d.icon = IconSprite(Guns + s.icon);
        d.projectileFx = AssetDatabase.LoadAssetAtPath<GameObject>((s.hovl ? HovlProj + "Projectile VFX loop/" : Fx) + s.projectile + ".prefab");
        d.muzzleFx = AssetDatabase.LoadAssetAtPath<GameObject>((s.hovl ? HovlProj + "Flash and hits/" : Fx) + s.muzzle + ".prefab");
        d.impactFx = AssetDatabase.LoadAssetAtPath<GameObject>((s.hovl ? HovlProj + "Flash and hits/" : Fx) + s.impact + ".prefab");
        if (s.beamFx != null)
        {
            d.beamFx = AssetDatabase.LoadAssetAtPath<GameObject>(Hovl + s.beamFx + ".prefab");
            if (d.beamFx == null) Debug.LogWarning("Weapons: laser prefab not found: " + Hovl + s.beamFx);
        }
        if (d.model == null) Debug.LogWarning("Weapons: model not found: " + Guns + s.model);
        if (d.projectileFx == null || d.muzzleFx == null || d.impactFx == null) Debug.LogWarning("Weapons: an effect prefab of " + s.name + " was not found (Epic Toon FX moved?)");
        if (fresh && d.model != null)
        {
            Measure(d.model, out Vector3 grip, out Vector3 muzzle);
            if (!float.IsNaN(s.muzzleY)) muzzle.y = s.muzzleY;
            d.grip = grip;
            d.muzzle = muzzle;
        }
        EditorUtility.SetDirty(d);
        return d;
    }

    /// The pack's icons are imported as plain textures; the HUD needs sprites.
    static Sprite IconSprite(string path)
    {
        var imp = AssetImporter.GetAtPath(path) as TextureImporter;
        if (imp == null) return null;
        if (imp.textureType != TextureImporterType.Sprite)
        {
            imp.textureType = TextureImporterType.Sprite;
            imp.spriteImportMode = SpriteImportMode.Single;
            imp.alphaIsTransparency = true;
            imp.mipmapEnabled = false;
            imp.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    /// Finds the grip and the muzzle of a gun model (barrel along +Z, grip down): the muzzle is the front-most
    /// part of the body, the grip sits between the bottom of the handle and the spot just behind the trigger.
    static void Measure(GameObject model, out Vector3 grip, out Vector3 muzzle)
    {
        MeshFilter body = null, trigger = null;
        foreach (var mf in model.GetComponentsInChildren<MeshFilter>(true))
            if (mf.sharedMesh != null && (body == null || mf.sharedMesh.vertexCount > body.sharedMesh.vertexCount)) body = mf;
        foreach (var mf in model.GetComponentsInChildren<MeshFilter>(true))
            if (mf != body && mf.sharedMesh != null) trigger = mf;
        grip = muzzle = Vector3.zero;
        if (body == null) return;

        var verts = body.sharedMesh.vertices;
        var loc = new Vector3[verts.Length];
        Vector3 mn = Vector3.one * 1e9f, mx = Vector3.one * -1e9f;
        for (int i = 0; i < verts.Length; i++)
        {
            loc[i] = model.transform.InverseTransformPoint(body.transform.TransformPoint(verts[i]));
            mn = Vector3.Min(mn, loc[i]);
            mx = Vector3.Max(mx, loc[i]);
        }
        Vector3 front = Vector3.zero, bottom = Vector3.zero;
        int nf = 0, nb = 0;
        foreach (var v in loc)
        {
            if (v.z > mx.z - 0.012f) { front += v; nf++; }
            if (v.y < mn.y + 0.02f) { bottom += v; nb++; }
        }
        muzzle = front / Mathf.Max(1, nf); muzzle.x = 0f;
        bottom /= Mathf.Max(1, nb); bottom.x = 0f;

        Vector3 top = new Vector3(0f, bottom.y + 0.1f, bottom.z + 0.02f);
        if (trigger != null)
        {
            Vector3 tmn = Vector3.one * 1e9f, tmx = Vector3.one * -1e9f;
            foreach (var v in trigger.sharedMesh.vertices)
            {
                var w = model.transform.InverseTransformPoint(trigger.transform.TransformPoint(v));
                tmn = Vector3.Min(tmn, w);
                tmx = Vector3.Max(tmx, w);
            }
            top = new Vector3(0f, (tmn.y + tmx.y) * 0.5f, tmn.z - 0.045f);
        }
        grip = Vector3.Lerp(bottom, top, 0.6f);
    }

    static void MakePickupPrefab(WeaponDef def)
    {
        if (def == null) return;
        string path = PrefabFolder + "/Pickup_" + def.name.Substring(7) + ".prefab";
        var go = new GameObject("Weapon Pickup (" + def.displayName + ")");
        var p = go.AddComponent<WeaponPickup>();
        p.weapon = def;
        p.visual = WeaponPickup.BuildVisual(go.transform, def, p.hoverHeight, p.modelScale);
        var so = new SerializedObject(p);
        so.FindProperty("visualOf").objectReferenceValue = def;
        so.ApplyModifiedPropertiesWithoutUndo();
        PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);
    }
}
