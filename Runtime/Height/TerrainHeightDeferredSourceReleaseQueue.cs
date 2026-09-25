using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Rendering;
using UnityEngine.ResourceManagement.AsyncOperations;

/*
 * Finishes GPU-safe Height source releases that outlive a streamer instance.
 * This is a resource-lifetime helper only; it owns no terrain/cache state.
 */
internal static class TerrainHeightDeferredSourceReleaseQueue
{
    private sealed class PendingRelease
    {
        public AsyncOperationHandle<Texture2D> Handle;
        public bool UsesFence;
        public GraphicsFence Fence;
        public int ReleaseFrame;
    }

    private static readonly List<PendingRelease> pending =
        new List<PendingRelease>();

    private static bool subscribed;

    public static void Enqueue(
        AsyncOperationHandle<Texture2D> handle,
        bool usesFence,
        GraphicsFence fence,
        int releaseFrame
    )
    {
        if (!handle.IsValid())
        {
            return;
        }

        pending.Add(
            new PendingRelease
            {
                Handle = handle,
                UsesFence = usesFence,
                Fence = fence,
                ReleaseFrame = releaseFrame
            }
        );

        EnsureSubscribed();
    }

    private static void EnsureSubscribed()
    {
        if (subscribed)
        {
            return;
        }

        Application.onBeforeRender += Poll;
        Application.quitting += ReleaseAll;
        subscribed = true;
    }

    private static void Poll()
    {
        for (int index = pending.Count - 1; index >= 0; index--)
        {
            PendingRelease item = pending[index];
            bool canRelease;

            if (item.UsesFence)
            {
                try
                {
                    canRelease = item.Fence.passed;
                }
                catch (Exception)
                {
                    item.UsesFence = false;
                    item.ReleaseFrame = Time.frameCount + 4;
                    canRelease = false;
                }
            }
            else
            {
                canRelease = Time.frameCount >= item.ReleaseFrame;
            }

            if (!canRelease)
            {
                continue;
            }

            pending.RemoveAt(index);

            if (item.Handle.IsValid())
            {
                Addressables.Release(item.Handle);
            }
        }

        if (pending.Count == 0)
        {
            Unsubscribe();
        }
    }

    private static void ReleaseAll()
    {
        for (int index = 0; index < pending.Count; index++)
        {
            if (pending[index].Handle.IsValid())
            {
                Addressables.Release(pending[index].Handle);
            }
        }

        pending.Clear();
        Unsubscribe();
    }

    private static void Unsubscribe()
    {
        if (!subscribed)
        {
            return;
        }

        Application.onBeforeRender -= Poll;
        Application.quitting -= ReleaseAll;
        subscribed = false;
    }
}
