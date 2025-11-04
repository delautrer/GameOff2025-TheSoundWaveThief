using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

public class FogOfWar : MonoBehaviour
{
    [Header("Referenzen")]
    public Tilemap tilemapFog;
    public Tilemap tilemapWalls;

    [Header("Sicht-Einstellungen")]
    public int sightRadius = 8;

    private HashSet<Vector3Int> clearedTiles = new HashSet<Vector3Int>();
    private Vector3Int lastPlayerPos;

    // [NEU] Diese Variable verhindert das "Start-Rennen"
    private bool isInitialized = false;

    void Start()
    {
        // Wir holen uns die Referenzen, aber machen noch NICHTS.
        if (tilemapFog == null)
            tilemapFog = GameObject.Find("Tilemap_Fog").GetComponent<Tilemap>();
        if (tilemapWalls == null)
            tilemapWalls = GameObject.Find("Tilemap_Walls").GetComponent<Tilemap>();

        // Initialisiere die Position, aber rufe UpdateFogOfWar() NICHT auf.
        lastPlayerPos = tilemapFog.WorldToCell(transform.position);
    }

    void Update()
    {
        Vector3Int currentPlayerPos = tilemapFog.WorldToCell(transform.position);

        // [NEU] Verzögerung um einen Frame
        // Wenn das Spiel noch nicht initialisiert ist, führe die ERSTE
        // Aufdeckung aus und setze die Variable auf 'true'.
        if (!isInitialized)
        {
            UpdateFogOfWar(currentPlayerPos);
            lastPlayerPos = currentPlayerPos;
            isInitialized = true;
            return; // Beende diesen Frame
        }

        // Normaler Update-Loop
        if (currentPlayerPos != lastPlayerPos)
        {
            UpdateFogOfWar(currentPlayerPos);
            lastPlayerPos = currentPlayerPos;
        }
    }

    void UpdateFogOfWar(Vector3Int playerPos)
    {
        for (int x = -sightRadius; x <= sightRadius; x++)
        {
            for (int y = -sightRadius; y <= sightRadius; y++)
            {
                Vector3Int targetPos = new Vector3Int(playerPos.x + x, playerPos.y + y, 0);

                if (Vector3Int.Distance(playerPos, targetPos) > sightRadius)
                    continue;

                // [GEÄNDERT] Diese Optimierung ist wichtig.
                // Wir decken nur auf, wenn wir *müssen*.
                if (!clearedTiles.Contains(targetPos) && HasLineOfSight(playerPos, targetPos))
                {
                    tilemapFog.SetTile(targetPos, null);
                    clearedTiles.Add(targetPos);
                }
            }
        }
    }

    bool HasLineOfSight(Vector3Int start, Vector3Int end)
    {
        int x0 = start.x, y0 = start.y;
        int x1 = end.x, y1 = end.y;

        int dx = Mathf.Abs(x1 - x0);
        int dy = -Mathf.Abs(y1 - y0);
        int sx = (x0 < x1) ? 1 : -1;
        int sy = (y0 < y1) ? 1 : -1;
        int err = dx + dy;

        while (true)
        {
            // [WICHTIG] Wir prüfen NICHT die Start-Kachel und NICHT die End-Kachel.
            Vector3Int currentPos = new Vector3Int(x0, y0, 0);
            if (currentPos != start && currentPos != end && tilemapWalls.HasTile(currentPos))
            {
                return false; // Wand blockiert die Sicht
            }

            if (x0 == x1 && y0 == y1) break;

            int e2 = 2 * err;
            if (e2 >= dy)
            {
                err += dy;
                x0 += sx;
            }
            if (e2 <= dx)
            {
                err += dx;
                y0 += sy;
            }
        }
        return true;
    }
}