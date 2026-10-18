import type { components } from "@/sdk/BApi2";
import type { IdName } from "@/components/types";
import type { SearchForm as ResourceSearchForm } from "@/pages/resource/models";

import { useCallback, useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { AiOutlineClose } from "react-icons/ai";
import { MdSavedSearch, MdEdit } from "react-icons/md";

import { Button, Input, Tooltip } from "@/components/bakaui";
import { useResourceOptionsStore } from "@/stores/options";
import { usePendingSearchStore } from "@/stores/pendingSearch";
import { FilterDisplayMode } from "@/sdk/constants";
import BApi from "@/sdk/BApi.tsx";
import {
  ResourceSourcesProvider,
  useResourceSources,
} from "@/features/management/resourceSourcesContext";
import { useManagementSources } from "@/features/management/useManagementSources";
import ResourceTabContent from "@/pages/resource/components/ResourceTabContent";
import RecentlyPlayedDrawer from "@/pages/resource/components/RecentlyPlayedDrawer";
import SearchSummary from "@/pages/resource/components/SearchSummary";
import { buildAutoTabName } from "@/pages/resource/utils/buildAutoTabName";
import { ResourceMovePanelDock } from "@/components/ResourceMovePanel";

type SearchForm = components["schemas"]["Bakabase.Service.Models.Input.ResourceSearchInputModel"];

const MaxMountedTabs = 10;
const EmptySearchForms: Record<string, ResourceSearchForm | undefined> = {};

const LocalResourcePage = () => {
  const { t } = useTranslation();
  const [savedSearches, setSavedSearches] = useState<IdName<string>[]>([]);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [editingName, setEditingName] = useState<string>("");
  const [activeSearchId, setActiveSearchId] = useState<string>();
  const [mountedTabIds, setMountedTabIds] = useState<string[]>([]);
  const [removingTabId, setRemovingTabId] = useState<string | null>(null);
  const [recentlyPlayedOpen, setRecentlyPlayedOpen] = useState(false);
  // Live search-form state per mounted tab, surfaced by ResourceTabContent so
  // the tab name can track filter edits in real time.
  const [liveSearchForms, setLiveSearchForms] = useState<
    Record<string, ResourceSearchForm | undefined>
  >({});
  // Load names and summaries for untouched tabs together, without mounting their resource grids.
  const [fetchedSearchState, setFetchedSearchState] = useState<{
    scope?: string;
    forms: Record<string, ResourceSearchForm | undefined>;
  }>({ forms: EmptySearchForms });
  const searchFormRequestRef = useRef<{
    scope: string;
    controller: AbortController;
    batchPending: boolean;
    requestedIds: Set<string>;
  }>();
  const resourceOptions = useResourceOptionsStore();
  const sourcesContext = useResourceSources();
  const localSource = sourcesContext?.sources.find((source) => source.isLocal);
  const searchScope = JSON.stringify([
    BApi.baseUrl,
    localSource?.serverId,
    localSource?.libraryEpoch,
  ]);
  const fetchedSearchForms =
    fetchedSearchState.scope === searchScope ? fetchedSearchState.forms : EmptySearchForms;
  const currentSearchScopeRef = useRef(searchScope);
  const savedSearchIdsRef = useRef(new Set<string>());

  currentSearchScopeRef.current = searchScope;
  savedSearchIdsRef.current = new Set(savedSearches.map((search) => search.id));
  const canLoadSearchForms =
    resourceOptions.initialized && !sourcesContext?.loading && savedSearches.length > 0;
  const pendingSearch = usePendingSearchStore((s) => s.pendingSearch);
  const consumePendingSearch = usePendingSearchStore((s) => s.consumePendingSearch);
  const optionsInitialized = useRef(false);

  const handleSearchFormChange = useCallback(
    (searchId: string, form: ResourceSearchForm | undefined) => {
      setLiveSearchForms((prev) => {
        if (Object.prototype.hasOwnProperty.call(prev, searchId) && prev[searchId] === form)
          return prev;

        return { ...prev, [searchId]: form };
      });
    },
    [],
  );

  // A failed batch or a newly created tab can retry on hover, without duplicating the initial batch.
  const ensureSearchForm = useCallback((searchId: string) => {
    const request = searchFormRequestRef.current;

    if (!request || request.batchPending || request.requestedIds.has(searchId)) return;
    request.requestedIds.add(searchId);

    BApi.resource
      .getSavedSearch(
        { id: searchId },
        { signal: request.controller.signal, showErrorToast: false },
      )
      .then((r) => {
        if (
          request.controller.signal.aborted ||
          searchFormRequestRef.current !== request ||
          currentSearchScopeRef.current !== request.scope ||
          !savedSearchIdsRef.current.has(searchId)
        )
          return;
        const search = r.data?.search;

        if (search) {
          setFetchedSearchState((prev) => ({
            scope: request.scope,
            forms: { ...prev.forms, [searchId]: search },
          }));
        } else {
          request.requestedIds.delete(searchId);
        }
      })
      .catch(() => {
        request.requestedIds.delete(searchId);
      });
  }, []);

  const creatingTabRef = useRef(false);
  const [isCreatingTab, setIsCreatingTab] = useState(false);
  const inputRef = useRef<HTMLInputElement | null>(null);

  useEffect(() => {
    if (editingId && inputRef.current) {
      inputRef.current.focus();
      inputRef.current.select();
    }
  }, [editingId]);

  const changeTab = useCallback((searchId: string) => {
    // LRU of mounted tabs: bounds memory while still caching recently used tabs.
    setMountedTabIds((prev) =>
      [searchId, ...prev.filter((id) => id !== searchId)].slice(0, MaxMountedTabs),
    );
    setActiveSearchId(searchId);
    // Save to localStorage for persistence across page refreshes
    localStorage.setItem("resource-active-tab-id", searchId);
  }, []);

  const handleOpenRecentlyPlayed = useCallback(() => setRecentlyPlayedOpen(true), []);

  const searchInNewTab = useCallback(
    async (f: SearchForm) => {
      if (creatingTabRef.current) return;
      creatingTabRef.current = true;
      setIsCreatingTab(true);

      try {
        const newSearch = (
          await BApi.resource.saveNewResourceSearch({
            search: f,
            displayMode: FilterDisplayMode.Simple,
          })
        ).data;

        if (!newSearch) return;

        setSavedSearches((prev) => [...prev, newSearch]);
        changeTab(newSearch.id);
      } finally {
        creatingTabRef.current = false;
        setIsCreatingTab(false);
      }
    },
    [changeTab],
  );

  // Initialize saved tabs and apply incoming searches in one place. An incoming
  // search must win over the default tab, including when options arrive later.
  useEffect(() => {
    if (!resourceOptions.initialized) return;
    let needsInitialTab = false;

    if (!optionsInitialized.current) {
      optionsInitialized.current = true;
      const saved = resourceOptions.data.savedSearches ?? [];

      setSavedSearches(saved);
      needsInitialTab = saved.length === 0;
      if (saved.length > 0) {
        const savedActiveTabId = localStorage.getItem("resource-active-tab-id");
        const savedTabExists = savedActiveTabId && saved.some((s) => s.id === savedActiveTabId);

        changeTab(savedTabExists ? savedActiveTabId : saved[0].id);
      }
    }

    // Leave a newly arrived search in the store while another tab is saving.
    // Completion changes isCreatingTab so it can be picked up without being lost.
    if (creatingTabRef.current) return;
    // Read and consume the live store value, not a closure from an earlier render.
    const incoming = consumePendingSearch();

    if (incoming || needsInitialTab) {
      void searchInNewTab(incoming ?? { page: 1, pageSize: 50 });
    }
  }, [
    resourceOptions.initialized,
    resourceOptions.data.savedSearches,
    pendingSearch,
    isCreatingTab,
    consumePendingSearch,
    searchInNewTab,
    changeTab,
  ]);

  useEffect(() => {
    setFetchedSearchState({ scope: searchScope, forms: EmptySearchForms });
    if (!canLoadSearchForms) return;
    const request = {
      scope: searchScope,
      controller: new AbortController(),
      batchPending: true,
      requestedIds: new Set<string>(),
    };

    searchFormRequestRef.current = request;
    BApi.resource
      .getSavedSearches({ signal: request.controller.signal, showErrorToast: false })
      .then((response) => {
        if (
          request.controller.signal.aborted ||
          searchFormRequestRef.current !== request ||
          currentSearchScopeRef.current !== request.scope
        )
          return;
        const forms: Record<string, ResourceSearchForm> = {};

        for (const search of response.data ?? []) {
          if (!savedSearchIdsRef.current.has(search.id)) continue;
          forms[search.id] = search.search;
          request.requestedIds.add(search.id);
        }
        setFetchedSearchState({ scope: request.scope, forms });
      })
      .catch(() => {
        // Individual hover reads remain available after an initial loading failure.
      })
      .finally(() => {
        request.batchPending = false;
      });

    return () => {
      request.controller.abort();
      if (searchFormRequestRef.current === request) searchFormRequestRef.current = undefined;
    };
  }, [canLoadSearchForms, searchScope]);

  // The displayed name uses the user-set name when present, otherwise an
  // auto-generated combination of current filter values or the batch-loaded criteria.
  // A live cleared form must take precedence over older saved filters.
  // When neither has filters it falls back to a localized
  // "Search N" placeholder so the tab still reads as something.
  const getDisplayName = useCallback(
    (s: IdName<string>, index: number): string => {
      if (s.name) return s.name;
      const form = Object.prototype.hasOwnProperty.call(liveSearchForms, s.id)
        ? liveSearchForms[s.id]
        : fetchedSearchForms[s.id];
      const auto = buildAutoTabName(form);

      if (auto) return auto;

      return `${t<string>("common.action.search")} ${index + 1}`;
    },
    [liveSearchForms, fetchedSearchForms, t],
  );

  const beginRename = (id: string) => {
    const index = savedSearches.findIndex((x) => x.id == id);

    if (index < 0) return;
    setEditingId(id);
    setEditingName(getDisplayName(savedSearches[index], index));
  };

  const commitRename = async () => {
    if (editingId == null) return;
    const name = editingName.trim();

    if (!name) {
      setEditingId(null);

      return;
    }
    setSavedSearches((prev) => {
      const next = prev.slice();

      const editingSearch = next.find((x) => x.id == editingId);

      if (editingSearch) {
        editingSearch.name = name;
      }

      return next;
    });
    try {
      await BApi.resource.putSavedSearchName(name, { id: editingId });
    } finally {
      setEditingId(null);
    }
  };

  const removeSaved = async (id: string) => {
    if (removingTabId) return;
    setRemovingTabId(id);

    try {
      await BApi.resource.deleteSavedSearch({ id });
      savedSearchIdsRef.current.delete(id);
      const idx = savedSearches.findIndex((x) => x.id == id);
      const prev = savedSearches[idx - 1];
      const next = savedSearches[idx + 1];

      setSavedSearches((prevSearches) => prevSearches.filter((s) => s.id != id));
      setMountedTabIds((prevIds) => prevIds.filter((mid) => mid !== id));

      const newActiveTab = (prev ?? next)?.id;

      if (newActiveTab) {
        changeTab(newActiveTab);
      } else {
        // If no tabs left, clear the saved active tab
        localStorage.removeItem("resource-active-tab-id");
      }
    } finally {
      setRemovingTabId(null);
    }
  };

  if (!resourceOptions.initialized) {
    return null;
  }

  const ss = savedSearches.slice();

  // console.log(activeSearchId, ss);

  return (
    <div className={"flex flex-col h-full max-h-full gap-1"}>
      <div className="flex items-center gap-2 border-b border-divider pb-1">
        <div className="grow min-w-0">
          {ss.length > 1 && (
            <div className="flex flex-wrap items-center gap-1">
              {ss.map((s, i) => {
                const isActive = activeSearchId == s.id || (activeSearchId == undefined && i == 0);
                const isEditing = editingId == s.id;
                // A mounted tab reports its criteria live, so its edits show up in
                // the tooltip without a round trip; everything else falls back to
                // the batch-loaded criteria.
                const liveForm = liveSearchForms[s.id];
                const hasLiveForm = Object.prototype.hasOwnProperty.call(liveSearchForms, s.id);
                const summaryForm = hasLiveForm ? liveForm : fetchedSearchForms[s.id];
                const revealSummary = () => {
                  if (!hasLiveForm) ensureSearchForm(s.id);
                };

                return (
                  <div
                    key={s.id}
                    className="group flex items-center"
                    onFocus={revealSummary}
                    onMouseEnter={revealSummary}
                  >
                    <Button
                      className="gap-1 pr-1"
                      color={isActive ? "primary" : "default"}
                      size="sm"
                      onPress={() => {
                        if (!isActive) {
                          changeTab(s.id);
                        }
                      }}
                    >
                      {/* The tab name is renameable, but nothing said so. Swapping the
                      saved-search glyph for a pencil on hover advertises it, and makes
                      the icon itself the click target so renaming no longer depends on
                      discovering the double-click. */}
                      <Tooltip content={t<string>("resource.tab.rename")} placement="top">
                        <div
                          className="relative w-[18px] h-[18px] shrink-0"
                          role="button"
                          tabIndex={0}
                          onClick={(e) => {
                            e.stopPropagation();
                            beginRename(s.id);
                          }}
                          onKeyDown={(e) => {
                            if (e.key === "Enter" || e.key === " ") {
                              e.preventDefault();
                              e.stopPropagation();
                              beginRename(s.id);
                            }
                          }}
                        >
                          <MdSavedSearch className="text-lg absolute inset-0 transition-opacity group-hover:opacity-0" />
                          <MdEdit className="text-lg absolute inset-0 opacity-0 transition-opacity group-hover:opacity-100" />
                        </div>
                      </Tooltip>
                      <div className="relative">
                        {/* The name is where the tab's identity lives, so it also
                        carries the search-criteria digest. Hanging it on the
                        name rather than the whole button keeps it from firing
                        at the same time as the rename hint on the icon. */}
                        <Tooltip
                          content={<SearchSummary form={summaryForm} loading={!summaryForm} />}
                          delay={300}
                          isDisabled={isEditing}
                          placement="bottom"
                        >
                          <span
                            className={`text-sm font-medium max-w-[150px] truncate block ${isEditing ? "invisible" : ""}`}
                            onDoubleClick={(e) => {
                              if (isActive) {
                                e.stopPropagation();
                                beginRename(s.id);
                              }
                            }}
                          >
                            {getDisplayName(s, i)}
                          </span>
                        </Tooltip>
                        {isEditing && (
                          <Input
                            ref={inputRef}
                            className="absolute inset-0"
                            classNames={{
                              input: "text-sm font-medium",
                              inputWrapper: "min-h-0 h-full px-0 bg-transparent shadow-none",
                              base: "h-full",
                            }}
                            size="sm"
                            value={editingName}
                            onBlur={() => {
                              if (editingId == s.id) commitRename();
                            }}
                            onClick={(e) => e.stopPropagation()}
                            onKeyDown={(e) => {
                              if (e.key === "Enter") {
                                e.preventDefault();
                                commitRename();
                              }
                              // Entering edit mode locks the name (per spec —
                              // even Escape commits the currently pre-filled
                              // auto-name). Clearing the input and committing
                              // is the explicit way back to auto-mode.
                              if (e.key === "Escape") {
                                e.preventDefault();
                                commitRename();
                              }
                            }}
                            onValueChange={(v) => setEditingName(v)}
                          />
                        )}
                      </div>
                      <div
                        className={`
                      p-0.5 rounded transition-opacity
                      ${
                        isActive
                          ? "opacity-70 hover:opacity-100 hover:bg-primary/30"
                          : "opacity-0 group-hover:opacity-60 hover:!opacity-100 hover:bg-default-300"
                      }
                    `}
                        role="button"
                        tabIndex={0}
                        onClick={(e) => {
                          e.stopPropagation();
                          removeSaved(s.id);
                        }}
                        onKeyDown={(e) => {
                          if (e.key === "Enter" || e.key === " ") {
                            e.preventDefault();
                            e.stopPropagation();
                            removeSaved(s.id);
                          }
                        }}
                      >
                        <AiOutlineClose className="text-xs" />
                      </div>
                    </Button>
                  </div>
                );
              })}
            </div>
          )}
        </div>
      </div>
      <div className="flex flex-1 min-h-0 min-w-0 overflow-hidden">
        <div className="flex flex-col flex-1 min-h-0 min-w-0">
          {ss.map((s) => {
            const isActive = s.id == activeSearchId;

            return (
              <div key={s.id} className={`grow min-h-0 ${isActive ? "" : "hidden"}`}>
                {mountedTabIds.includes(s.id) && (
                  <ResourceTabContent
                    activated={isActive}
                    searchId={s.id}
                    searchInNewTab={searchInNewTab}
                    searchName={getDisplayName(s, ss.indexOf(s))}
                    onOpenRecentlyPlayed={handleOpenRecentlyPlayed}
                    onSearchFormChange={handleSearchFormChange}
                  />
                )}
              </div>
            );
          })}
        </div>
        <ResourceMovePanelDock />
      </div>
      <RecentlyPlayedDrawer
        isOpen={recentlyPlayedOpen}
        onClose={() => setRecentlyPlayedOpen(false)}
      />
    </div>
  );
};

/** The same resource page renders local and managed resources; only the data scope changes. */
const ResourcePage = () => {
  const management = useManagementSources();

  return (
    <ResourceSourcesProvider value={management}>
      <LocalResourcePage />
    </ResourceSourcesProvider>
  );
};

export default ResourcePage;
