using UnityEditor;

internal sealed class AssetSyncService : AssetPostprocessor
{
    private static void OnPostprocessAllAssets(
        string[] importedAssets,
        string[] deletedAssets,
        string[] movedAssets,
        string[] movedFromAssetPaths)
    {
        CollaborationSession session = CollaborationTool.SharedSession;
        if (!session.ShouldPublishAssetEvents) return;

        for (int i = 0; i < movedAssets.Length; i++)
            session.PublishMovedAsset(movedFromAssetPaths[i], movedAssets[i]);
        foreach (string path in deletedAssets)
            session.PublishDeletedAsset(path);
        foreach (string path in importedAssets)
            session.PublishImportedAsset(path);
    }
}
