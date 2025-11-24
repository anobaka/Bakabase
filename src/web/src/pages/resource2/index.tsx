import type { components } from "@/sdk/BApi2";
import type { IdName } from "@/components/types";

import { useCallback, useEffect, useRef, useState } from "react";
import { Button, Tab, Tabs } from "@heroui/react";
import { Input } from "@/components/bakaui";
import { useTranslation } from "react-i18next";
import { AiOutlineClose } from "react-icons/ai";

import { useResourceOptionsStore } from "@/stores/options";
import BApi from "@/sdk/BApi.tsx";
import ResourceTabContent from "@/pages/resource2/components/ResourceTabContent.tsx";

type SavedSearch =
  components["schemas"]["Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain.ResourceOptions+SavedSearch"];

type SearchForm = components["schemas"]["Bakabase.Modules.Search.Models.Db.ResourceSearchDbModel"];

const ResourcePage2 = () => {
  const { t } = useTranslation();
  const [savedSearches, setSavedSearches] = useState<IdName<string>[]>([]);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [editingName, setEditingName] = useState<string>("");
  const [activeSearchId, setActiveSearchId] = useState<string>();
  const resourceOptions = useResourceOptionsStore();
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

  const searchInNewTab = async (f: SearchForm) => {
    const nrs = {
      search: f,
      // id: uuidv4().slice(0, 6),
    };

    const newSearch = (await BApi.resource.saveNewResourceSearch(nrs)).data!;

    setSavedSearches((prev) => [...prev, newSearch]);
    changeTab(newSearch.id);
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
    // if (!confirm(t("Are you sure to delete this tab?"))) return;
    await BApi.resource.deleteSavedSearch({ id });
    const idx = savedSearches.findIndex((x) => x.id == id);
    const prev = savedSearches[idx - 1];
    const next = savedSearches[idx + 1];

    setSavedSearches((prev) => prev.filter((s) => s.id != id));
    changeTab((prev ?? next)?.id);
  };

  console.log("saved searches", savedSearches);

  if (!resourceOptions.initialized) {
    return null;
  }

  const ss = savedSearches.slice();

  // console.log(activeSearchId, ss);

  return (
    <div className={"flex flex-col h-full max-h-full "}>
      <Tabs
        classNames={{ panel: "grow py-0 px-0", tabList: ss.length == 1 ? "hidden" : "" }}
        destroyInactiveTabPanel={false}
        selectedKey={activeSearchId}
        variant="underlined"
        onSelectionChange={(key) => {
          changeTab(key as string);
        }}
      >
        {ss.map((s, i) => (
          <Tab
            key={s.id}
            className={"w-auto"}
            title={
              <div className="flex items-center gap-2 mb-2">
                {activeSearchId == s.id || (activeSearchId == undefined && i == 0) ? (
                  <>
                    {editingId == s.id ? (
                      <Input
                        ref={inputRef}
                        size="sm"
                        className="max-w-xs w-auto min-w-auto"
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
                      />
                    ) : (
                      <div
                        key={s.id}
                        className="max-w-xs w-auto min-w-auto outline-none"
                        onClick={(e) => {
                          if (activeSearchId == s.id && editingId != s.id) {
                            e.preventDefault();
                            beginRename(s.id);
                          }
                        }}
                      >
                        {s.name}
                      </div>
                    )}
                  </>
                ) : (
                  s.name
                )}
                <Button
                  isIconOnly
                  variant="light"
                  onPress={() => {
                    removeSaved(s.id);
                  }}
                  size="sm"
                  // color="danger"
                  className="opacity-60"
                >
                  <AiOutlineClose className={"text-sm"} />
                </Button>
              </div>
            }
          />
        ))}
      </Tabs>
      {ss.map((s) => {
        const isActive = s.id == activeSearchId;

        return (
          <div key={s.id} className={`grow ${isActive ? "" : "hidden"}`}>
            {activatedSearchIds.current.has(s.id) && (
              <ResourceTabContent
                activated={isActive}
                searchId={s.id}
                searchInNewTab={searchInNewTab}
              />
            )}
          </div>
        );
      })}
    </div>
  );
};

export default ResourcePage2;
