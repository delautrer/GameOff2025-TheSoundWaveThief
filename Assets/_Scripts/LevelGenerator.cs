using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

public class LevelGenerator : MonoBehaviour
{
    // Struktur (bleibt gleich)
    public struct GeneratedStructure
    {
        public RectInt rect;
        public bool isCorridor;

        public GeneratedStructure(RectInt rect, bool isCorridor)
        {
            this.rect = rect;
            this.isCorridor = isCorridor;
        }
    }

    [Header("Tilemap Referenzen")]
    public Tilemap tilemapFloor;
    public Tilemap tilemapWalls;
    public Tilemap tilemapFog;

    [Header("Tile Referenzen")]
    public TileBase tileFloor;
    public TileBase tileWall; 
    public TileBase tileFog;

    [Header("Spieler Referenz")]
    public Transform player;

    [Header("Raum-Generator Einstellungen")]
    public int numberOfRooms = 20;
    public Vector2Int minRoomSize = new Vector2Int(15, 15);
    public Vector2Int maxRoomSize = new Vector2Int(25, 25);
    public int maxGenerationAttempts = 1000;

    [Header("Flur-Generator Einstellungen")]
    [Range(0.0f, 1.0f)]
    public float corridorChance = 0.3f;
    public Vector2Int corridorMinMaxSize = new Vector2Int(2, 4);
    public Vector2Int corridorMinMaxLength = new Vector2Int(15, 25);

    [Header("Ausstellungsstück-Generator")]
    public GameObject exhibitPrefab;
    public int exhibitWallSpacing = 5;
    public int exhibitMinSpacing = 5;
    public int doorMinSpacing = 5;

    // --- Private Variablen ---
    private List<GeneratedStructure> generatedStructures = new List<GeneratedStructure>();
    private HashSet<Vector2Int> doorPositions = new HashSet<Vector2Int>();
    private List<Vector2Int> exhibitSpawnPoints = new List<Vector2Int>();
    private HashSet<Vector2Int> floorPositions = new HashSet<Vector2Int>();
    private List<Vector2Int> directions = new List<Vector2Int>
    { 
        Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right,
        new Vector2Int(1, 1),
        new Vector2Int(1, -1),
        new Vector2Int(-1, -1),
        new Vector2Int(-1, 1)
    };

    void Start()
    {
        GenerateLevel();
    }

    // [GEÄNDERT] Neue Funktion 'FillEmptyRooms' hinzugefügt
    void GenerateLevel()
    {
        Debug.Log("Starte Raum-Generierung (mit Füll-Schritt)...");

        ClearMap();
        generatedStructures.Clear();
        doorPositions.Clear();
        exhibitSpawnPoints.Clear();
        floorPositions.Clear();

        CreateStartingRoom();
        CreateAdditionalRooms();
        PruneDeadEnds();
        BuildFloorSet();

        GenerateExhibitPoints(); // 1. Versuche, Muster zu platzieren
        FillEmptyRooms();        // 2. [NEU] Fülle leere Räume auf

        PaintAllRooms();
        PaintAllWalls();
        PaintInitialFog();

        SpawnExhibitPrefabs();

        Debug.Log($"Level generiert mit {generatedStructures.Count} Strukturen und {exhibitSpawnPoints.Count} Ausstellungsstücken.");
    }

    void BuildFloorSet()
    {
        foreach (GeneratedStructure structure in generatedStructures)
        {
            for (int x = structure.rect.xMin; x < structure.rect.xMax; x++)
            {
                for (int y = structure.rect.yMin; y < structure.rect.yMax; y++)
                {
                    floorPositions.Add(new Vector2Int(x, y));
                }
            }
        }
        floorPositions.UnionWith(doorPositions);
    }

    void GenerateExhibitPoints()
    {
        for (int i = 1; i < generatedStructures.Count; i++) // Startraum überspringen
        {
            GeneratedStructure structure = generatedStructures[i];
            if (structure.isCorridor) continue; // Flure überspringen

            RectInt room = structure.rect;

            RectInt spawnableBounds = new RectInt(
                room.xMin + exhibitWallSpacing,
                room.yMin + exhibitWallSpacing,
                room.width - (exhibitWallSpacing * 2),
                room.height - (exhibitWallSpacing * 2)
            );

            if (spawnableBounds.width <= 0 || spawnableBounds.height <= 0)
            {
                continue;
            }

            bool isWide = spawnableBounds.width > spawnableBounds.height * 1.5f;
            bool isTall = spawnableBounds.height > spawnableBounds.width * 1.5f;

            List<Vector2Int> potentialPoints = new List<Vector2Int>();

            if (isTall)
            {
                int leftLineX = spawnableBounds.xMin;
                int rightLineX = spawnableBounds.xMax - 1;
                for (int y = spawnableBounds.yMin; y < spawnableBounds.yMax; y += exhibitMinSpacing)
                {
                    potentialPoints.Add(new Vector2Int(leftLineX, y));
                    potentialPoints.Add(new Vector2Int(rightLineX, y));
                }
            }
            else if (isWide)
            {
                int bottomLineY = spawnableBounds.yMin;
                int topLineY = spawnableBounds.yMax - 1;
                for (int x = spawnableBounds.xMin; x < spawnableBounds.xMax; x += exhibitMinSpacing)
                {
                    potentialPoints.Add(new Vector2Int(x, bottomLineY));
                    potentialPoints.Add(new Vector2Int(x, topLineY));
                }
            }
            else
            {
                int midX = Mathf.RoundToInt(spawnableBounds.center.x);
                int midY = Mathf.RoundToInt(spawnableBounds.center.y);
                int halfSpacing = Mathf.CeilToInt(exhibitMinSpacing / 2f);

                potentialPoints.Add(new Vector2Int(midX - halfSpacing, midY - halfSpacing));
                potentialPoints.Add(new Vector2Int(midX + halfSpacing, midY - halfSpacing));
                potentialPoints.Add(new Vector2Int(midX - halfSpacing, midY + halfSpacing));
                potentialPoints.Add(new Vector2Int(midX + halfSpacing, midY + halfSpacing));
            }

            foreach (Vector2Int point in potentialPoints)
            {
                if (CheckExhibitRules(point))
                {
                    exhibitSpawnPoints.Add(point);
                }
            }
        }
    }

    // [NEU] Diese Funktion füllt Räume auf, die von GenerateExhibitPoints leer gelassen wurden
    void FillEmptyRooms()
    {
        // Gehe alle Räume durch (überspringe Startraum i=0)
        for (int i = 1; i < generatedStructures.Count; i++)
        {
            GeneratedStructure structure = generatedStructures[i];

            // Überspringe Flure
            if (structure.isCorridor) continue;

            // 1. Prüfe, ob dieser Raum bereits ein Ausstellungsstück hat
            bool hasExhibit = false;
            foreach (Vector2Int exhibitPos in exhibitSpawnPoints)
            {
                if (structure.rect.Contains(exhibitPos))
                {
                    hasExhibit = true;
                    break; // Ja, hat eins. Nächsten Raum prüfen.
                }
            }

            // 2. Wenn der Raum leer ist, füge eins in der Mitte hinzu
            if (!hasExhibit)
            {
                // Finde die Mitte des Raums
                Vector2Int centerPos = new Vector2Int(
                    Mathf.RoundToInt(structure.rect.center.x),
                    Mathf.RoundToInt(structure.rect.center.y)
                );

                // 3. Prüfe, ob die Mitte ein GÜLTIGER Ort ist (z.B. nicht zu nah an einer Tür)
                //    (Die 'CheckExhibitRules' prüft Abstand zu Türen und anderen Stücken)
                if (CheckExhibitRules(centerPos))
                {
                    exhibitSpawnPoints.Add(centerPos);
                }
                // Wenn selbst die Mitte ungültig ist (z.B. Raum voller Türen),
                // bleibt der Raum (korrekterweise) leer.
            }
        }
    }


    bool CheckExhibitRules(Vector2Int tilePos)
    {
        // REGEL 1 (Nicht bei Türen)
        foreach (Vector2Int doorPos in doorPositions)
        {
            if (Vector2.Distance(tilePos, doorPos) < doorMinSpacing)
            {
                return false;
            }
        }

        // REGEL 3 (Nicht zu nah aneinander)
        foreach (Vector2Int exhibitPos in exhibitSpawnPoints)
        {
            if (Vector2.Distance(tilePos, exhibitPos) < exhibitMinSpacing)
            {
                return false;
            }
        }

        // REGEL 2 (Muss auf dem Boden sein)
        if (!floorPositions.Contains(tilePos))
        {
            return false;
        }

        return true;
    }

    void SpawnExhibitPrefabs()
    {
        if (exhibitPrefab == null)
        {
            Debug.LogError("Exhibit Prefab ist nicht zugewiesen!");
            return;
        }

        foreach (Vector2Int pos in exhibitSpawnPoints)
        {
            Vector3 spawnPos = new Vector3(pos.x + 0.5f, pos.y + 0.5f, 0);
            Instantiate(exhibitPrefab, spawnPos, Quaternion.identity, this.transform);
        }
    }

    void ClearMap()
    {
        tilemapFloor.ClearAllTiles();
        tilemapWalls.ClearAllTiles();
        tilemapFog.ClearAllTiles();

        foreach (Transform child in transform)
        {
            Destroy(child.gameObject);
        }
    }

    void PaintAllRooms()
    {
        foreach (GeneratedStructure structure in generatedStructures)
        {
            for (int x = structure.rect.xMin; x < structure.rect.xMax; x++)
            {
                for (int y = structure.rect.yMin; y < structure.rect.yMax; y++)
                {
                    Vector3Int tilePos = new Vector3Int(x, y, 0);
                    tilemapFloor.SetTile(tilePos, tileFloor);
                }
            }
        }

        foreach (Vector2Int doorPos in doorPositions)
        {
            tilemapFloor.SetTile((Vector3Int)doorPos, tileFloor);
        }
    }

    void PaintAllWalls()
    {
        foreach (Vector2Int floorPos in floorPositions)
        {
            foreach (Vector2Int direction in directions)
            {
                Vector2Int neighborPos = floorPos + direction;

                if (!floorPositions.Contains(neighborPos))
                {
                    tilemapWalls.SetTile(new Vector3Int(neighborPos.x, neighborPos.y, 0), tileWall);
                }
            }
        }
    }
        void PaintInitialFog()
    {
        if (tilemapFog == null || tileFog == null) return;

        BoundsInt floorBounds = tilemapFloor.cellBounds;

        int buffer = 5;

        for (int x = floorBounds.xMin - buffer; x < floorBounds.xMax + buffer; x++)
        {
            for (int y = floorBounds.yMin - buffer; y < floorBounds.yMax + buffer; y++)
            {
                tilemapFog.SetTile(new Vector3Int(x, y, 0), tileFog);
            }
        }
    }

    void CreateStartingRoom()
    {
        int startWidth = Random.Range(minRoomSize.x, maxRoomSize.x);
        int startHeight = Random.Range(minRoomSize.y, maxRoomSize.y);
        int startX = -startWidth / 2;
        int startY = -startHeight / 2;
        RectInt startRoomRect = new RectInt(startX, startY, startWidth, startHeight);

        generatedStructures.Add(new GeneratedStructure(startRoomRect, false));

        if (player != null)
        {
            Vector2Int playerSpawnPos = new Vector2Int(Mathf.RoundToInt(startRoomRect.center.x), Mathf.RoundToInt(startRoomRect.center.y));
            player.position = new Vector3(playerSpawnPos.x, playerSpawnPos.y, 0);
        }
    }

    void CreateAdditionalRooms()
    {
        int currentAttempts = 0;

        while (generatedStructures.Count < numberOfRooms && currentAttempts < maxGenerationAttempts)
        {
            currentAttempts++;

            GeneratedStructure existingStructure = generatedStructures[Random.Range(0, generatedStructures.Count)];
            Vector2Int direction = GetRandomDirection();

            bool buildCorridor = (Random.value < corridorChance);

            if (buildCorridor && existingStructure.isCorridor)
            {
                continue;
            }

            int newWidth;
            int newHeight;

            if (buildCorridor)
            {
                bool isVertical = (Random.value > 0.5f);
                if (isVertical)
                {
                    newWidth = Random.Range(corridorMinMaxSize.x, corridorMinMaxSize.y);
                    newHeight = Random.Range(corridorMinMaxLength.x, corridorMinMaxLength.y);
                }
                else
                {
                    newWidth = Random.Range(corridorMinMaxLength.x, corridorMinMaxLength.y);
                    newHeight = Random.Range(corridorMinMaxSize.x, corridorMinMaxSize.y);
                }
            }
            else
            {
                newWidth = Random.Range(minRoomSize.x, maxRoomSize.x);
                newHeight = Random.Range(minRoomSize.y, maxRoomSize.y);
            }

            RectInt newRoomRect = new RectInt();

            if (direction == Vector2Int.up)
            {
                int xPos = existingStructure.rect.x + (existingStructure.rect.width / 2) - (newWidth / 2);
                int yPos = existingStructure.rect.yMax + 1;
                newRoomRect = new RectInt(xPos, yPos, newWidth, newHeight);
            }
            else if (direction == Vector2Int.down)
            {
                int xPos = existingStructure.rect.x + (existingStructure.rect.width / 2) - (newWidth / 2);
                int yPos = existingStructure.rect.yMin - newHeight - 1;
                newRoomRect = new RectInt(xPos, yPos, newWidth, newHeight);
            }
            else if (direction == Vector2Int.right)
            {
                int xPos = existingStructure.rect.xMax + 1;
                int yPos = existingStructure.rect.y + (existingStructure.rect.height / 2) - (newHeight / 2);
                newRoomRect = new RectInt(xPos, yPos, newWidth, newHeight);
            }
            else if (direction == Vector2Int.left)
            {
                int xPos = existingStructure.rect.xMin - newWidth - 1;
                int yPos = existingStructure.rect.y + (existingStructure.rect.height / 2) - (newHeight / 2);
                newRoomRect = new RectInt(xPos, yPos, newWidth, newHeight);
            }

            if (!CheckOverlap(newRoomRect, buildCorridor))
            {
                generatedStructures.Add(new GeneratedStructure(newRoomRect, buildCorridor));
                CreateDoor(existingStructure.rect, newRoomRect, direction);
            }
        }

        if (currentAttempts >= maxGenerationAttempts)
        {
            Debug.LogWarning("Maximale Generierungs-Versuche erreicht. Stoppe frühzeitig.");
        }
    }

    void PruneDeadEnds()
    {
        bool deadEndWasPruned = true;
        int safetyBreak = 0;

        while (deadEndWasPruned && safetyBreak < 100)
        {
            deadEndWasPruned = false;
            safetyBreak++;

            Dictionary<int, int> doorCount = new Dictionary<int, int>();
            for (int i = 0; i < generatedStructures.Count; i++)
            {
                doorCount[i] = 0;
            }

            foreach (Vector2Int doorPos in doorPositions)
            {
                for (int i = 0; i < generatedStructures.Count; i++)
                {
                    foreach (Vector2Int dir in directions)
                    {
                        Vector2Int neighborPos = doorPos + dir;
                        if (generatedStructures[i].rect.Contains(neighborPos))
                        {
                            doorCount[i]++;
                            break;
                        }
                    }
                }
            }

            for (int i = generatedStructures.Count - 1; i >= 0; i--)
            {
                GeneratedStructure structure = generatedStructures[i];
                int count = doorCount[i];

                if (structure.isCorridor && count == 1 && generatedStructures.Count > 1)
                {
                    List<Vector2Int> doorsToRemove = new List<Vector2Int>();
                    foreach (Vector2Int doorPos in doorPositions)
                    {
                        foreach (Vector2Int dir in directions)
                        {
                            if (structure.rect.Contains(doorPos + dir))
                            {
                                doorsToRemove.Add(doorPos);
                                break;
                            }
                        }
                    }

                    foreach (Vector2Int door in doorsToRemove)
                    {
                        doorPositions.Remove(door);
                    }

                    generatedStructures.RemoveAt(i);
                    deadEndWasPruned = true;
                    break;
                }
            }
        }

        if (safetyBreak >= 100)
        {
            Debug.LogError("PruneDeadEnds hat die maximale Iteration erreicht. Breche ab.");
        }
    }

    Vector2Int GetRandomDirection()
    {
        int rand = Random.Range(0, 4);
        if (rand == 0) return Vector2Int.up;
        if (rand == 1) return Vector2Int.down;
        if (rand == 2) return Vector2Int.left;
        return Vector2Int.right;
    }

    bool CheckOverlap(RectInt room, bool isNewRoomACorridor)
    {
        RectInt bufferRoom = new RectInt(room.x - 1, room.y - 1, room.width + 2, room.height + 2);
        RectInt corridorCheckBuffer = new RectInt(room.x - 2, room.y - 2, room.width + 4, room.height + 4);

        foreach (GeneratedStructure existing in generatedStructures)
        {
            if (existing.rect.Overlaps(bufferRoom))
            {
                return true;
            }

            if (isNewRoomACorridor && existing.isCorridor)
            {
                if (existing.rect.Overlaps(corridorCheckBuffer))
                {
                    return true;
                }
            }
        }
        return false;
    }

    void CreateDoor(RectInt roomA, RectInt roomB, Vector2Int direction)
    {
        int doorX = 0;
        int doorY = 0;
        int overlapMin = 0;
        int overlapMax = 0;

        if (direction == Vector2Int.up || direction == Vector2Int.down)
        {
            overlapMin = Mathf.Max(roomA.xMin, roomB.xMin);
            overlapMax = Mathf.Min(roomA.xMax, roomB.xMax);

            if (overlapMin >= overlapMax - 1)
            {
                doorX = overlapMin;
            }
            else
            {
                doorX = Random.Range(overlapMin + 1, overlapMax - 1);
            }
            doorY = (direction == Vector2Int.up) ? roomA.yMax : roomA.yMin - 1;
        }
        else
        {
            overlapMin = Mathf.Max(roomA.yMin, roomB.yMin);
            overlapMax = Mathf.Min(roomA.yMax, roomB.yMax);

            if (overlapMin >= overlapMax - 1)
            {
                doorY = overlapMin;
            }
            else
            {
                doorY = Random.Range(overlapMin + 1, overlapMax - 1);
            }
            doorX = (direction == Vector2Int.right) ? roomA.xMax : roomA.xMin - 1;
        }
        doorPositions.Add(new Vector2Int(doorX, doorY));
    }
}