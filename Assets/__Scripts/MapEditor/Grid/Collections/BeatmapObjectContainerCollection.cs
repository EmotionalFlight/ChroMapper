﻿using System.Collections.Generic;
using UnityEngine;
using System;
using System.Linq;

public abstract class BeatmapObjectContainerCollection : MonoBehaviour
{
    public static int ChunkSize = 5;
    public static string TrackFilterID { get; private set; } = null;

    private static Dictionary<BeatmapObject.Type, BeatmapObjectContainerCollection> loadedCollections = new Dictionary<BeatmapObject.Type, BeatmapObjectContainerCollection>();

    public AudioTimeSyncController AudioTimeSyncController;
    public List<BeatmapObjectContainer> LoadedContainers = new List<BeatmapObjectContainer>();
    public BeatmapObjectCallbackController SpawnCallbackController;
    public BeatmapObjectCallbackController DespawnCallbackController;
    public Transform GridTransform;
    public bool UseChunkLoading = true;
    public bool UseChunkLoadingWhenPlaying = false;
    public bool IgnoreTrackFilter;
    private float previousATSCBeat = -1;
    private int previousChunk = -1;
    private bool levelLoaded;

    public abstract BeatmapObject.Type ContainerType { get; }

    public static BeatmapObjectContainerCollection GetAnyCollection() => GetCollectionForType<NotesContainer>(BeatmapObject.Type.NOTE);

    public static BeatmapObjectContainerCollection GetCollectionForType(BeatmapObject.Type type)
    {
        loadedCollections.TryGetValue(type, out BeatmapObjectContainerCollection collection);
        return collection;
    }

    public static T GetCollectionForType<T>(BeatmapObject.Type type) where T : BeatmapObjectContainerCollection
    {
        loadedCollections.TryGetValue(type, out BeatmapObjectContainerCollection collection);
        return collection as T;
    }

    private void OnEnable()
    {
        BeatmapObjectContainer.FlaggedForDeletionEvent += DeleteObject;
        LoadInitialMap.LevelLoadedEvent += LevelHasLoaded;
        loadedCollections.Add(ContainerType, this);
        SubscribeToCallbacks();
    }

    private void LevelHasLoaded()
    {
        levelLoaded = true;
    }

    public void RemoveConflictingObjects()
    {
        List<BeatmapObjectContainer> old = new List<BeatmapObjectContainer>(LoadedContainers);
        foreach (BeatmapObjectContainer stayedAlive in LoadedContainers.DistinctBy(x => x.objectData.ConvertToJSON()))
        {
            old.Remove(stayedAlive);
        }
        foreach (BeatmapObjectContainer conflicting in old)
        {
            DeleteObject(conflicting, false);
        }
        Debug.Log($"Removed {old.Count} conflicting objects.");
    }

    public void DeleteObject(BeatmapObjectContainer obj, bool triggersAction = true, string comment = "No comment.")
    {
        if (LoadedContainers.Contains(obj))
        {
            if (triggersAction) BeatmapActionContainer.AddAction(new BeatmapObjectDeletionAction(obj, comment));
            LoadedContainers.Remove(obj);
            Destroy(obj.gameObject);
            SelectionController.RefreshMap();
        }
    }

    internal virtual void LateUpdate()
    {
        if ((AudioTimeSyncController.IsPlaying && !UseChunkLoadingWhenPlaying)
            || !UseChunkLoading
            || AudioTimeSyncController.CurrentBeat == previousATSCBeat
            || !levelLoaded) return;
        previousATSCBeat = AudioTimeSyncController.CurrentBeat;
        int nearestChunk = (int)Math.Round(previousATSCBeat / (double)ChunkSize, MidpointRounding.AwayFromZero);
        if (nearestChunk != previousChunk)
        {
            UpdateChunks(nearestChunk);
            previousChunk = nearestChunk;
        }
    }

    protected void UpdateChunks(int nearestChunk)
    {
        int distance = AudioTimeSyncController.IsPlaying ? 2 : Settings.Instance.ChunkDistance;
        foreach (BeatmapObjectContainer e in LoadedContainers)
        {
            int chunkID = e.ChunkID;
            bool isWall = e is BeatmapObstacleContainer;
            BeatmapObstacleContainer o = isWall ? e as BeatmapObstacleContainer : null;
            if ((!isWall && chunkID < nearestChunk - distance) || (isWall && o?.ChunkEnd < nearestChunk - distance))
            {
                if (BoxSelectionPlacementController.IsSelecting) continue;
                e.SafeSetActive(false);
                continue;
            }
            if (chunkID > nearestChunk + distance)
            {
                if (BoxSelectionPlacementController.IsSelecting) continue;
                e.SafeSetActive(false);
                continue;
            }
            if (TrackFilterID != null)
            {
                if ((e.objectData._customData?["track"] ?? "") != TrackFilterID && !IgnoreTrackFilter)
                {
                    if (BoxSelectionPlacementController.IsSelecting) continue;
                    e.SafeSetActive(false);
                    continue;
                }
            }
            e.SafeSetActive(true);
        }
    }

    private void OnDisable()
    {
        BeatmapObjectContainer.FlaggedForDeletionEvent -= DeleteObject;
        LoadInitialMap.LevelLoadedEvent -= LevelHasLoaded;
        loadedCollections.Remove(ContainerType);
        UnsubscribeToCallbacks();
    }

    protected void SetTrackFilter()
    {
        PersistentUI.Instance.ShowInputBox("Filter notes and obstacles shown while editing to a certain track ID.\n\n" +
            "If you dont know what you're doing, turn back now.", HandleTrackFilter);
    }

    private void HandleTrackFilter(string res)
    {
        TrackFilterID = (string.IsNullOrEmpty(res) || string.IsNullOrWhiteSpace(res)) ? null : res;
        SendMessage("UpdateChunks");
    }

    protected bool ConflictingByTrackIDs(BeatmapObject a, BeatmapObject b)
    {
        if (a._customData is null && b._customData is null) return true; //Both dont exist, they are conflicting (default track)
        if (a._customData is null || b._customData is null) return false; //One exists, but not other; they dont conflict
        if (a._customData["track"] is null && b._customData["track"] is null) return true; //Both dont exist, they are conflicting
        if (a._customData["track"] is null || b._customData["track"] is null) return false; //One exists, but not other
        return a._customData["track"].Value == b._customData["track"].Value; //If both exist, check string values.
    }

    internal abstract void SubscribeToCallbacks();
    internal abstract void UnsubscribeToCallbacks();
    public abstract void SortObjects();
    public abstract BeatmapObjectContainer SpawnObject(BeatmapObject obj, out BeatmapObjectContainer conflicting, bool removeConflicting = true, bool refreshMap = true);
    public BeatmapObjectContainer SpawnObject(BeatmapObject obj, bool removeConflicting = true, bool refreshMap = true) => SpawnObject(obj, out _, removeConflicting, refreshMap);
}
