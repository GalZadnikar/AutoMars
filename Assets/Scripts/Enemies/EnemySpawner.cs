using System.Collections.Generic;
using UnityEngine;

/// Place by hand in the scene (Tools > Trash Game > 13. Enemies > Add Enemy Spawner). Keeps up to `maxAlive` enemies
/// of one kind around it and refills after `respawnTime` seconds once one is killed.
public class EnemySpawner : MonoBehaviour
{
    public enum Kind { Scout, Drone, Spider }

    public Kind kind = Kind.Scout;
    public int maxAlive = 2;
    [Tooltip("Seconds until a killed enemy is replaced")]
    public float respawnTime = 45f;
    [Tooltip("Enemies appear within this distance of the spawner")]
    public float spawnRadius = 3f;
    [Tooltip("Drones only: patrol area (the drone's home) around the spawner")]
    public float homeRadius = 10f;
    [Tooltip("Drones only: how far from home they chase")]
    public float leashRadius = 20f;
    public bool spawnOnStart = true;

    public int Alive => alive.Count;

    readonly List<GameObject> alive = new List<GameObject>();
    float timer;
    bool filled;
    int lastCount;

    void Start() { timer = spawnOnStart ? 0f : respawnTime; }

    void Update()
    {
        alive.RemoveAll(g => g == null || !g.activeInHierarchy);
        if (alive.Count < lastCount && filled) timer = respawnTime;   // something died: the refill timer starts
        lastCount = alive.Count;
        if (alive.Count >= maxAlive) { filled = true; return; }
        timer -= Time.deltaTime;
        if (timer > 0f) return;
        SpawnOne();
        timer = filled ? respawnTime : 0.25f;
        lastCount = alive.Count;
    }

    public GameObject SpawnOne()
    {
        Vector2 r = Random.insideUnitCircle * spawnRadius;
        Vector3 pos = transform.position + new Vector3(r.x, 0f, r.y);
        GameObject go;
        if (kind == Kind.Scout) go = EnemyScout.Spawn(pos).gameObject;
        else if (kind == Kind.Spider)
        {
            var sp = EnemySpider.Spawn(pos);
            if (sp == null) return null;
            go = sp.gameObject;
        }
        else
        {
            var d = EnemyDrone.Spawn(pos, transform.position);
            d.homeRadius = homeRadius;
            d.leashRadius = leashRadius;
            go = d.gameObject;
        }
        alive.Add(go);
        return go;
    }

    void OnDrawGizmos()
    {
        Gizmos.color = kind == Kind.Scout ? new Color(1f, 0.6f, 0.2f) : kind == Kind.Spider ? new Color(0.6f, 0.9f, 0.2f) : new Color(1f, 0.2f, 0.2f);
        Gizmos.DrawWireSphere(transform.position, spawnRadius);
        Gizmos.DrawSphere(transform.position + Vector3.up * 0.5f, 0.3f);
        if (kind == Kind.Drone)
        {
            Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.5f);
            Gizmos.DrawWireSphere(transform.position, homeRadius);
        }
    }
}
