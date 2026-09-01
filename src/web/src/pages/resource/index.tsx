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
import BApi from "@/sdk/BApi.tsx";
import ResourceTabContent from "@/pages/resource/components/ResourceTabContent";
import RecentlyPlayedDrawer from "@/pages/resource/components/RecentlyPlayedDrawer";
import SearchSummary from "@/pages/resource/components/SearchSummary";
import { buildAutoTabName } from "@/pages/resource/utils/buildAutoTabName";

type SavedSearch =
  components["schemas"]["Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain.ResourceOptions+SavedSearch"];

type SearchForm = components["schemas"]["Bakabase.Modules.Search.Models.Db.ResourceSearchDbModel"];

const MaxMountedTabs = 10;

const ResourcePage = () => {
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
  // Criteria of tabs that aren't mounted — only the active tab is on first
  // paint, so without this every other tab's tooltip would have nothing to
  // show. Fetched on hover rather than up front so a long tab bar doesn't
  // fire a request per tab while the page is still loading its resources.
  const [fetchedSearchForms, setFetchedSearchForms] = useState<
    Record<string, ResourceSearchForm | undefined>
  >({});
  const requestedSearchIdsRef = useRef(new Set<string>());
  const resourceOptions = useResourceOptionsStore();
  const pendingSearch = usePendingSearchStore((s) => s.pendingSearch);
  const consumePendingSearch = usePendingSearchStore((s) => s.consumePendingSearch);
  const optionsInitialized = useRef(false);

  const handleSearchFormChange = useCallback(
    (searchId: string, form: ResourceSearchForm | undefined) => {
      setLiveSearchForms((prev) => {
        if (prev[searchId] === form) return prev;

        return { ...prev, [searchId]: form };
      });
    },
    [],
  );

  // Pulls a tab's criteria for the tooltip. The saved-search endpoint returns
  // filters already resolved to property names and biz values, which the
  // options store's raw copy is not. A failed request leaves nothing behind so
  // the next hover retries.
  const ensureSearchForm = useCallback((searchId: string) => {
    if (requestedSearchIdsRef.current.has(searchId)) return;
    requestedSearchIdsRef.current.add(searchId);

    BApi.resource
      .getSavedSearch({ id: searchId })
      .then((r) => {
        const search = r.data?.search;

        if (search) {
          setFetchedSearchForms((prev) => ({ ...prev, [searchId]: search }));
        } else {
          requestedSearchIdsRef.current.delete(searchId);
        }
      })
      .catch(() => {
        requestedSearchIdsRef.current.delete(searchId);
      });
  }, []);

  const creatingTabRef = useRef(false);
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

  // Handle initial options loading
  useEffect(() => {
    if (optionsInitialized.current) {
      return;
    }
    if (resourceOptions.initialized) {
      const ss1 = resourceOptions.data.savedSearches ?? [];

      if (ss1.length == 0) {
        searchInNewTab({ page: 1, pageSize: 50 });
      } else {
        setSavedSearches(ss1);

        // Try to restore the previously active tab from localStorage
        const savedActiveTabId = localStorage.getItem("resource-active-tab-id");
        const savedTabExists = savedActiveTabId && ss1.some((s) => s.id === savedActiveTabId);

        // Use saved tab if it exists, otherwise default to first tab
        changeTab(savedTabExists ? savedActiveTabId : ss1[0].id);
      }
      optionsInitialized.current = true;
    }
  }, [resourceOptions.initialized]);

  // Handle pending search from store (e.g., from ChildrenModal)
  useEffect(() => {
    if (!optionsInitialized.current) {
      return;
    }
    if (pendingSearch) {
      consumePendingSearch(); // Clear the pending search
      searchInNewTab(pendingSearch);
    }
  }, [pendingSearch]);

  const searchInNewTab = useCallback(
    async (f: SearchForm) => {
      if (creatingTabRef.current) return;
      creatingTabRef.current = true;

      try {
        const newSearch = (await BApi.resource.saveNewResourceSearch({ search: f })).data;

        if (!newSearch) return;

        setSavedSearches((prev) => [...prev, newSearch]);
        changeTab(newSearch.id);
      } finally {
        creatingTabRef.current = false;
      }
    },
    [changeTab],
  );

  // The displayed name uses the user-set name when present, otherwise an
  // auto-generated combination of the current filter values reported by the
  // mounted ResourceTabContent. When neither is available (e.g. un-mounted
  // tab beyond the LRU cap, or no filters yet) it falls back to a localized
  // "Search N" placeholder so the tab still reads as something.
  const getDisplayName = useCallback(
    (s: IdName<string>, index: number): string => {
      if (s.name) return s.name;
      const auto = buildAutoTabName(liveSearchForms[s.id]);

      if (auto) return auto;

      return `${t<string>("common.action.search")} ${index + 1}`;
    },
    [liveSearchForms, t],
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
      {ss.length > 1 && (
        <div className="flex flex-wrap items-center gap-1 pb-2 border-b border-divider">
          {ss.map((s, i) => {
            const isActive = activeSearchId == s.id || (activeSearchId == undefined && i == 0);
            const isEditing = editingId == s.id;
            // A mounted tab reports its criteria live, so its edits show up in
            // the tooltip without a round trip; everything else falls back to
            // the copy fetched on hover.
            const liveForm = liveSearchForms[s.id];
            const summaryForm = liveForm ?? fetchedSearchForms[s.id];
            const revealSummary = () => {
              if (!liveForm) ensureSearchForm(s.id);
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
                  onPress={() => {
                    if (!isActive) {
                      changeTab(s.id);
                    }
                  }}
                  size="sm"
                  // variant="flat"
                  color={isActive ? "primary" : "default"}
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
      {ss.map((s) => {
        const isActive = s.id == activeSearchId;

        return (
          <div key={s.id} className={`grow min-h-0 ${isActive ? "" : "hidden"}`}>
            {mountedTabIds.includes(s.id) && (
              <ResourceTabContent
                activated={isActive}
                searchId={s.id}
                searchInNewTab={searchInNewTab}
                onOpenRecentlyPlayed={handleOpenRecentlyPlayed}
                onSearchFormChange={handleSearchFormChange}
              />
            )}
          </div>
        );
      })}
      <RecentlyPlayedDrawer
        isOpen={recentlyPlayedOpen}
        onClose={() => setRecentlyPlayedOpen(false)}
      />
    </div>
  );
};

export default ResourcePage;
