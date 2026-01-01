import type { components } from "@/sdk/BApi2";
import type { IdName } from "@/components/types";

import { useCallback, useEffect, useRef, useState } from "react";
import { Button, Input } from "@/components/bakaui";
import { useTranslation } from "react-i18next";
import { AiOutlineClose } from "react-icons/ai";

import { useResourceOptionsStore } from "@/stores/options";
import { usePendingSearchStore } from "@/stores/pendingSearch";
import BApi from "@/sdk/BApi.tsx";
import ResourceTabContent from "@/pages/resource/components/ResourceTabContent";
import RecentlyPlayedDrawer from "@/pages/resource/components/RecentlyPlayedDrawer";

type SavedSearch =
  components["schemas"]["Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain.ResourceOptions+SavedSearch"];

type SearchForm = components["schemas"]["Bakabase.Modules.Search.Models.Db.ResourceSearchDbModel"];

const ResourcePage = () => {
  const { t } = useTranslation();
  const [savedSearches, setSavedSearches] = useState<IdName<string>[]>([]);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [editingName, setEditingName] = useState<string>("");
  const [activeSearchId, setActiveSearchId] = useState<string>();
  const [creatingTab, setCreatingTab] = useState(false);
  const [removingTabId, setRemovingTabId] = useState<string | null>(null);
  const [recentlyPlayedOpen, setRecentlyPlayedOpen] = useState(false);
  const resourceOptions = useResourceOptionsStore();
  const pendingSearch = usePendingSearchStore((s) => s.pendingSearch);
  const consumePendingSearch = usePendingSearchStore((s) => s.consumePendingSearch);
  const optionsInitialized = useRef(false);

  const activatedSearchIds = useRef(new Set<string>());
  const inputRef = useRef<HTMLInputElement | null>(null);

  useEffect(() => {
    if (editingId && inputRef.current) {
      inputRef.current.focus();
      inputRef.current.select();
    }
  }, [editingId]);

  const changeTab = useCallback((searchId: string) => {
    activatedSearchIds.current.add(searchId);
    setActiveSearchId(searchId);
  }, []);

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
        changeTab(ss1[0].id);
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

  const searchInNewTab = async (f: SearchForm) => {
    if (creatingTab) return;
    setCreatingTab(true);

    try {
      const nrs = {
        search: f,
        // id: uuidv4().slice(0, 6),
      };

      const newSearch = (await BApi.resource.saveNewResourceSearch(nrs)).data;
      if (!newSearch) return;

      setSavedSearches((prev) => [...prev, newSearch]);
      changeTab(newSearch.id);
    } finally {
      setCreatingTab(false);
    }
  };

  const beginRename = (id: string) => {
    const current = savedSearches.find((x) => x.id == id);

    setEditingId(id);
    setEditingName(current?.name ?? "");
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

      setSavedSearches((prev) => prev.filter((s) => s.id != id));
      changeTab((prev ?? next)?.id);
    } finally {
      setRemovingTabId(null);
    }
  };

  console.log("saved searches", savedSearches);

  if (!resourceOptions.initialized) {
    return null;
  }

  const ss = savedSearches.slice();

  // console.log(activeSearchId, ss);

  return (
    <div className={"flex flex-col h-full max-h-full gap-1"}>
      {ss.length > 1 && (
        <div className="flex flex-wrap items-center gap-1 py-2 border-b border-divider">
          {ss.map((s, i) => {
            const isActive = activeSearchId == s.id || (activeSearchId == undefined && i == 0);
            const isEditing = editingId == s.id;

            return (
              <div key={s.id} className="group flex items-center">
                <Button
                  size="sm"
                  // variant="flat"
                  color={isActive ? "primary" : "default"}
                  className="gap-1 pr-1"
                  onPress={() => {
                    if (!isActive) {
                      changeTab(s.id);
                    }
                  }}
                >
                  <div className="relative">
                    <span
                      className={`text-sm font-medium max-w-[150px] truncate block ${isEditing ? "invisible" : ""}`}
                      onDoubleClick={(e) => {
                        if (isActive) {
                          e.stopPropagation();
                          beginRename(s.id);
                        }
                      }}
                    >
                      {s.name}
                    </span>
                    {isEditing && (
                      <Input
                        ref={inputRef}
                        size="sm"
                        className="absolute inset-0"
                        classNames={{
                          input: "text-sm font-medium",
                          inputWrapper: "min-h-0 h-full px-0 bg-transparent shadow-none",
                          base: "h-full",
                        }}
                        value={editingName}
                        onValueChange={(v) => setEditingName(v)}
                        onBlur={() => {
                          if (editingId == s.id) commitRename();
                        }}
                        onKeyDown={(e) => {
                          if (e.key === "Enter") {
                            e.preventDefault();
                            commitRename();
                          }
                          if (e.key === "Escape") {
                            e.preventDefault();
                            setEditingId(null);
                          }
                        }}
                        onClick={(e) => e.stopPropagation()}
                      />
                    )}
                  </div>
                  <div
                    className={`
                      p-0.5 rounded transition-opacity
                      ${isActive
                        ? "opacity-70 hover:opacity-100 hover:bg-primary/30"
                        : "opacity-0 group-hover:opacity-60 hover:!opacity-100 hover:bg-default-300"
                      }
                    `}
                    onClick={(e) => {
                      e.stopPropagation();
                      removeSaved(s.id);
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
          <div key={s.id} className={`grow ${isActive ? "" : "hidden"}`}>
            {activatedSearchIds.current.has(s.id) && (
              <ResourceTabContent
                activated={isActive}
                searchId={s.id}
                searchInNewTab={searchInNewTab}
                onOpenRecentlyPlayed={() => setRecentlyPlayedOpen(true)}
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
