"use client";

import React, { useState } from "react";
import { DataOrigin, DataStatus, ResourceAdditionalItem, ResourceDataType, ResourceStatus } from "@/sdk/constants";
import type { PlayableItem, Resource, ResourceDataState } from "@/core/models/Resource";
import ResourceCover from "@/components/Resource/components/ResourceCover";
import PlayControl from "@/components/Resource/components/PlayControl";
import type { PlayControlPortalProps } from "@/components/Resource/components/PlayControl";
import { Button, Card } from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import { useCoverResolution, type CoverResolutionState, type CoverResolutionCallbacks } from "@/hooks/useCoverResolution";
import { usePlayableItemResolution, type PlayableItemResolutionState, type PlayableItemResolutionCallbacks } from "@/hooks/usePlayableItemResolution";

// ── Helpers ──────────────────────────────────────────────────────────────────

const baseResource = (overrides: Partial<Resource> = {}): Resource => ({
  id: Math.floor(Math.random() * 100000),
  mediaLibraryId: 1,
  categoryId: 1,
  status: ResourceStatus.Active,
  fileName: "test-resource",
  path: "/fake/path/test-resource",
  hasChildren: false,
  isFile: false,
  createdAt: new Date().toISOString(),
  updatedAt: new Date().toISOString(),
  fileCreatedAt: new Date().toISOString(),
  fileModifiedAt: new Date().toISOString(),
  pinned: false,
  tags: [],
  ...overrides,
});

const ds = (dataType: ResourceDataType, origin: DataOrigin, status: DataStatus, resourceId = 1): ResourceDataState => ({
  resourceId, dataType, origin, status,
});

const pi = (origin: DataOrigin, key: string, displayName?: string): PlayableItem => ({
  origin, key, displayName,
});

type LogEntry = { time: string; msg: string };

function useDiscoveryLog() {
  const [logs, setLogs] = useState<LogEntry[]>([]);
  const addLog = (msg: string) => {
    setLogs(prev => [...prev, { time: new Date().toLocaleTimeString(), msg }]);
  };
  const makeCoverCallbacks = (): CoverResolutionCallbacks => ({
    onDiscoveryStart: (id, origin) =>
      addLog(`SSE Start: rid=${id}, origin=${DataOrigin[origin]}`),
    onDiscoveryComplete: (id, origin, data, error) => {
      if (error) {
        addLog(`SSE Error: rid=${id}, origin=${DataOrigin[origin]}, ${error}`);
      } else {
        addLog(`SSE Done: rid=${id}, origin=${DataOrigin[origin]}, covers=${JSON.stringify(data?.coverPaths ?? null)}`);
      }
    },
  });
  const makePlayCallbacks = (): PlayableItemResolutionCallbacks => ({
    onDiscoveryStart: (id, origin) =>
      addLog(`SSE Start: rid=${id}, origin=${DataOrigin[origin]}`),
    onDiscoveryComplete: (id, origin, data, error) => {
      if (error) {
        addLog(`SSE Error: rid=${id}, origin=${DataOrigin[origin]}, ${error}`);
      } else {
        const items = data?.playableItems?.map(i => `${DataOrigin[i.origin]}:${i.key}`);
        addLog(`SSE Done: rid=${id}, origin=${DataOrigin[origin]}, items=[${items?.join(", ") ?? ""}]`);
      }
    },
  });
  return { logs, addLog, makeCoverCallbacks, makePlayCallbacks };
}

// ── Test Cases ───────────────────────────────────────────────────────────────

type TestCase = {
  title: string;
  description: string;
  resource: Resource;
};

const coverCases: TestCase[] = [
  {
    title: "No dataStates, no covers",
    description: "Phase 1: dataStates null, covers null. Should trigger SSE.",
    resource: baseResource({ dataStates: undefined, covers: undefined }),
  },
  {
    title: "All cover providers NotStarted",
    description: "Providers exist but none ready. Should trigger SSE.",
    resource: baseResource({
      dataStates: [
        ds(ResourceDataType.Cover, DataOrigin.Manual, DataStatus.NotStarted),
        ds(ResourceDataType.Cover, DataOrigin.FileSystem, DataStatus.NotStarted),
      ],
    }),
  },
  {
    title: "Manual cover Ready, has covers",
    description: "User set cover. Should display immediately, no SSE.",
    resource: baseResource({
      covers: ["/fake/cover/manual.jpg"],
      dataStates: [
        ds(ResourceDataType.Cover, DataOrigin.Manual, DataStatus.Ready),
        ds(ResourceDataType.Cover, DataOrigin.FileSystem, DataStatus.Ready),
      ],
    }),
  },
  {
    title: "Steam cover Ready, has covers (with path)",
    description: "Steam cover downloaded. Resource has path so FileSystem CoverProvider applies. FileSystem NotStarted → SSE should trigger for FileSystem.",
    resource: baseResource({
      covers: ["/fake/cover/steam-header.jpg"],
      dataStates: [
        ds(ResourceDataType.Cover, DataOrigin.Manual, DataStatus.Ready),
        ds(ResourceDataType.Cover, DataOrigin.Steam, DataStatus.Ready),
        ds(ResourceDataType.Cover, DataOrigin.FileSystem, DataStatus.NotStarted),
      ],
    }),
  },
  {
    title: "Steam cover Ready, has covers (no path)",
    description: "Steam cover downloaded. Resource has no path so FileSystem CoverProvider doesn't apply. No FileSystem dataState. No SSE should trigger.",
    resource: baseResource({
      path: undefined as unknown as string,
      covers: ["/fake/cover/steam-header.jpg"],
      dataStates: [
        ds(ResourceDataType.Cover, DataOrigin.Manual, DataStatus.Ready),
        ds(ResourceDataType.Cover, DataOrigin.Steam, DataStatus.Ready),
      ],
    }),
  },
  {
    title: "All Ready, no covers found",
    description: "All checked, none found. Should show fallback.",
    resource: baseResource({
      covers: undefined,
      dataStates: [
        ds(ResourceDataType.Cover, DataOrigin.Manual, DataStatus.Ready),
        ds(ResourceDataType.Cover, DataOrigin.FileSystem, DataStatus.Ready),
      ],
    }),
  },
  {
    title: "FileSystem Ready with cover",
    description: "Local discovery found cover. No SSE needed.",
    resource: baseResource({
      covers: ["/fake/cover/local.jpg"],
      dataStates: [
        ds(ResourceDataType.Cover, DataOrigin.Manual, DataStatus.Ready),
        ds(ResourceDataType.Cover, DataOrigin.FileSystem, DataStatus.Ready),
      ],
    }),
  },
];

const playCases: TestCase[] = [
  {
    title: "No dataStates, no playableItems",
    description: "Phase 1: nothing loaded yet.",
    resource: baseResource({ dataStates: undefined, playableItems: undefined }),
  },
  {
    title: "FS PlayableItem NotStarted",
    description: "FS not ready. Should idle then trigger SSE on hover.",
    resource: baseResource({
      dataStates: [ds(ResourceDataType.PlayableItem, DataOrigin.FileSystem, DataStatus.NotStarted)],
    }),
  },
  {
    title: "FS PlayableItem Ready, has items",
    description: "FS cache ready. No SSE needed.",
    resource: baseResource({
      playableItems: [
        pi(DataOrigin.FileSystem, "/fake/path/video.mp4", "video.mp4"),
        pi(DataOrigin.FileSystem, "/fake/path/game.exe", "game.exe"),
      ],
      dataStates: [ds(ResourceDataType.PlayableItem, DataOrigin.FileSystem, DataStatus.Ready)],
    }),
  },
  {
    title: "Steam only",
    description: "Steam item, no FS items.",
    resource: baseResource({
      playableItems: [pi(DataOrigin.Steam, "12345", "Half-Life 3")],
      dataStates: [
        ds(ResourceDataType.PlayableItem, DataOrigin.Steam, DataStatus.Ready),
        ds(ResourceDataType.PlayableItem, DataOrigin.FileSystem, DataStatus.NotStarted),
      ],
    }),
  },
  {
    title: "Steam + DLsite + FS all ready",
    description: "Multi-source. No SSE needed.",
    resource: baseResource({
      playableItems: [
        pi(DataOrigin.Steam, "12345", "Steam Game"),
        pi(DataOrigin.DLsite, "RJ123456", "DLsite Work"),
        pi(DataOrigin.FileSystem, "/fake/path/game.exe", "game.exe"),
      ],
      dataStates: [
        ds(ResourceDataType.PlayableItem, DataOrigin.Steam, DataStatus.Ready),
        ds(ResourceDataType.PlayableItem, DataOrigin.DLsite, DataStatus.Ready),
        ds(ResourceDataType.PlayableItem, DataOrigin.FileSystem, DataStatus.Ready),
      ],
    }),
  },
  {
    title: "All Ready, no items",
    description: "All checked, nothing found.",
    resource: baseResource({
      playableItems: undefined,
      dataStates: [ds(ResourceDataType.PlayableItem, DataOrigin.FileSystem, DataStatus.Ready)],
    }),
  },
  {
    title: "File resource (isFile=true)",
    description: "Single file resource.",
    resource: baseResource({
      isFile: true,
      playableItems: [pi(DataOrigin.FileSystem, "/fake/path/movie.mkv", "movie.mkv")],
      dataStates: [ds(ResourceDataType.PlayableItem, DataOrigin.FileSystem, DataStatus.Ready)],
    }),
  },
];

// ── Simple PlayButton portal for testing ─────────────────────────────────────

const TestPlayPortal: React.FC<PlayControlPortalProps> = (props) => {
  const { status, sources, fsDiscoveryStatus } = props;
  return (
    <div className="p-2 border rounded text-xs space-y-1">
      <div><strong>Status:</strong> {status}</div>
      <div><strong>FS Discovery:</strong> {fsDiscoveryStatus}</div>
      <div><strong>Sources:</strong> {sources.length === 0 ? "none" : sources.map(s => `${DataOrigin[s.source]}(${s.items.length})`).join(", ")}</div>
      {sources.map(s => (
        <div key={s.source} className="ml-2">
          <Button size="sm" onPress={() => props.onPlaySource(s.source)}>
            Play {DataOrigin[s.source]}
          </Button>
        </div>
      ))}
    </div>
  );
};

// ── SSE status badge ─────────────────────────────────────────────────────────

const CoverStatusPanel: React.FC<{ resolution: CoverResolutionState; logs: LogEntry[] }> = ({ resolution, logs }) => (
  <div className="mt-2 text-[10px] space-y-0.5">
    <div className="flex gap-2 items-center">
      <span className={`inline-block w-2 h-2 rounded-full ${
        resolution.status === "ready" ? "bg-success" :
        resolution.status === "loading" ? "bg-warning animate-pulse" :
        "bg-default-300"
      }`} />
      <span>Cover: <strong>{resolution.status}</strong></span>
    </div>
    {logs.length > 0 && (
      <div className="bg-default-100 rounded p-1 max-h-20 overflow-auto font-mono">
        {logs.map((l, i) => (
          <div key={i} className="text-default-600">[{l.time}] {l.msg}</div>
        ))}
      </div>
    )}
  </div>
);

const PlayStatusPanel: React.FC<{ resolution: PlayableItemResolutionState; logs: LogEntry[] }> = ({ resolution, logs }) => (
  <div className="mt-2 text-[10px] space-y-0.5">
    <div className="flex gap-2 items-center">
      <span className={`inline-block w-2 h-2 rounded-full ${
        resolution.overallStatus === "ready" ? "bg-success" :
        resolution.overallStatus === "loading" ? "bg-warning animate-pulse" :
        "bg-default-300"
      }`} />
      <span>Play: <strong>{resolution.overallStatus}</strong> ({resolution.groups.length} groups)</span>
    </div>
    {logs.length > 0 && (
      <div className="bg-default-100 rounded p-1 max-h-20 overflow-auto font-mono">
        {logs.map((l, i) => (
          <div key={i} className="text-default-600">[{l.time}] {l.msg}</div>
        ))}
      </div>
    )}
  </div>
);

// ── Instrumented case wrappers ───────────────────────────────────────────────

const CoverTestCase: React.FC<{ tc: TestCase }> = ({ tc }) => {
  const { logs, makeCoverCallbacks } = useDiscoveryLog();
  const resolution = useCoverResolution(tc.resource, makeCoverCallbacks());

  return (
    <Card className="p-4 space-y-2">
      <h3 className="font-semibold text-sm">{tc.title}</h3>
      <p className="text-xs text-default-500">{tc.description}</p>
      <div className="border rounded p-2 bg-default-50">
        <div className="w-40 h-56 relative">
          <ResourceCover resource={tc.resource} />
        </div>
      </div>
      <CoverStatusPanel resolution={resolution} logs={logs} />
      <details className="text-xs">
        <summary className="cursor-pointer text-default-400">DataStates</summary>
        <pre className="text-[10px] bg-default-100 p-1 rounded overflow-auto max-h-20">
          {JSON.stringify(tc.resource.dataStates ?? "null", null, 2)}
        </pre>
      </details>
    </Card>
  );
};

const PlayTestCase: React.FC<{ tc: TestCase }> = ({ tc }) => {
  const { logs, makePlayCallbacks } = useDiscoveryLog();
  const resolution = usePlayableItemResolution(tc.resource, makePlayCallbacks());

  return (
    <Card className="p-4 space-y-2">
      <h3 className="font-semibold text-sm">{tc.title}</h3>
      <p className="text-xs text-default-500">{tc.description}</p>
      <div className="border rounded p-2 bg-default-50">
        <PlayControl resource={tc.resource} PortalComponent={TestPlayPortal} />
      </div>
      <PlayStatusPanel resolution={resolution} logs={logs} />
      <details className="text-xs">
        <summary className="cursor-pointer text-default-400">DataStates</summary>
        <pre className="text-[10px] bg-default-100 p-1 rounded overflow-auto max-h-20">
          {JSON.stringify(tc.resource.dataStates ?? "null", null, 2)}
        </pre>
      </details>
    </Card>
  );
};

// ── Live SSE Test ────────────────────────────────────────────────────────────

const LiveSseTest: React.FC = () => {
  const [resourceId, setResourceId] = useState<string>("");
  const [resource, setResource] = useState<Resource | null>(null);
  const [loading, setLoading] = useState(false);
  const [log, setLog] = useState<LogEntry[]>([]);
  const [testKey, setTestKey] = useState(0);

  const addLog = (msg: string) => {
    setLog(prev => [...prev, { time: new Date().toLocaleTimeString(), msg }]);
  };

  const resetAndLoad = async () => {
    const id = parseInt(resourceId);
    if (isNaN(id) || id <= 0) return;

    setLoading(true);
    setResource(null);
    setLog([]);
    setTestKey(prev => prev + 1);

    try {
      addLog("Loading resource...");

      addLog("Loading resource (additionalItems=None)...");
      const rsp = await BApi.resource.getResourcesByKeys({ ids: [id], additionalItems: ResourceAdditionalItem.None });
      const resources = (rsp as any)?.data ?? [];
      if (resources.length === 0) {
        addLog("ERROR: Resource not found.");
        setLoading(false);
        return;
      }

      const r = resources[0] as Resource;
      addLog(`Loaded: id=${r.id}, path=${r.path}`);
      addLog(`covers=${JSON.stringify(r.covers ?? null)}, dataStates=${JSON.stringify(r.dataStates ?? null)}`);
      addLog("Rendering...");
      setResource(r);
    } catch (e: any) {
      addLog(`ERROR: ${e.message}`);
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="space-y-4">
      <h2 className="text-lg font-bold">Live SSE Discovery Test</h2>
      <p className="text-xs text-default-500">
        Enter a real resource ID. "Reset & Test" clears cache and reloads without Cover/PlayableItem data. Repeatable.
      </p>

      <div className="flex gap-2 items-end">
        <div>
          <label className="text-xs text-default-500 block mb-1">Resource ID</label>
          <input
            type="number"
            value={resourceId}
            onChange={e => setResourceId(e.target.value)}
            className="border rounded px-2 py-1 w-32 text-sm"
            placeholder="e.g. 123"
          />
        </div>
        <Button color="primary" isLoading={loading} onPress={resetAndLoad} isDisabled={!resourceId || loading}>
          Reset & Test
        </Button>
      </div>

      {resource && (
        <LiveSseTestCards key={testKey} resource={resource} parentLog={log} addParentLog={addLog} />
      )}

      {log.length > 0 && (
        <Card className="p-3">
          <h3 className="font-semibold text-xs mb-1">Global Log</h3>
          <pre className="text-[10px] bg-default-100 p-2 rounded max-h-48 overflow-auto font-mono">
            {log.map((l, i) => <div key={i}>[{l.time}] {l.msg}</div>)}
          </pre>
        </Card>
      )}
    </div>
  );
};

/** Separate component so hooks are stable across re-renders */
const LiveSseTestCards: React.FC<{
  resource: Resource;
  parentLog: LogEntry[];
  addParentLog: (msg: string) => void;
}> = ({ resource, addParentLog }) => {
  const coverCallbacks: CoverResolutionCallbacks = {
    onDiscoveryStart: (id, origin) => addParentLog(`[Cover SSE] Start: rid=${id}, origin=${DataOrigin[origin]}`),
    onDiscoveryComplete: (id, origin, data, error) =>
      error
        ? addParentLog(`[Cover SSE] Error: origin=${DataOrigin[origin]}, ${error}`)
        : addParentLog(`[Cover SSE] Done: origin=${DataOrigin[origin]}, coverPaths=${JSON.stringify(data?.coverPaths ?? null)}`),
  };
  const playCallbacks: PlayableItemResolutionCallbacks = {
    onDiscoveryStart: (id, origin) => addParentLog(`[Play SSE] Start: rid=${id}, origin=${DataOrigin[origin]}`),
    onDiscoveryComplete: (id, origin, data, error) =>
      error
        ? addParentLog(`[Play SSE] Error: origin=${DataOrigin[origin]}, ${error}`)
        : addParentLog(`[Play SSE] Done: origin=${DataOrigin[origin]}, items=${data?.playableItems?.length ?? 0}`),
  };

  const coverResolution = useCoverResolution(resource, coverCallbacks);
  const playResolution = usePlayableItemResolution(resource, playCallbacks);

  return (
    <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
      <Card className="p-4 space-y-2">
        <h3 className="font-semibold text-sm">ResourceCover</h3>
        <CoverStatusPanel resolution={coverResolution} logs={[]} />
        <div className="w-40 h-56 relative border rounded">
          <ResourceCover resource={resource} />
        </div>
      </Card>
      <Card className="p-4 space-y-2">
        <h3 className="font-semibold text-sm">PlayControl</h3>
        <PlayStatusPanel resolution={playResolution} logs={[]} />
        <PlayControl resource={resource} PortalComponent={TestPlayPortal} />
      </Card>
    </div>
  );
};

// ── Page ─────────────────────────────────────────────────────────────────────

const PlayControlAndCoverTest: React.FC = () => {
  const [selectedSection, setSelectedSection] = useState<"cover" | "play" | "live">("cover");

  return (
    <div className="p-4 space-y-4">
      <h1 className="text-xl font-bold">PlayControl & ResourceCover Test Cases</h1>

      <div className="flex gap-2">
        <Button color={selectedSection === "cover" ? "primary" : "default"} onPress={() => setSelectedSection("cover")}>
          Cover ({coverCases.length})
        </Button>
        <Button color={selectedSection === "play" ? "primary" : "default"} onPress={() => setSelectedSection("play")}>
          PlayControl ({playCases.length})
        </Button>
        <Button color={selectedSection === "live" ? "primary" : "default"} onPress={() => setSelectedSection("live")}>
          Live SSE
        </Button>
      </div>

      {selectedSection === "live" ? (
        <LiveSseTest />
      ) : selectedSection === "cover" ? (
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
          {coverCases.map((tc, idx) => <CoverTestCase key={idx} tc={tc} />)}
        </div>
      ) : (
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
          {playCases.map((tc, idx) => <PlayTestCase key={idx} tc={tc} />)}
        </div>
      )}
    </div>
  );
};

export default PlayControlAndCoverTest;
