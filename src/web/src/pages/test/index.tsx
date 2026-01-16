"use client";

import React, { useEffect } from "react";
import "./index.scss";
import { useTranslation } from "react-i18next";
import { ListboxItem, Button } from "@heroui/react";
import { useCookie, useLocalStorage } from "react-use";
import { ChevronLeftIcon, ChevronRightIcon } from "@heroui/shared-icons";

import PscPage from "./cases/Psc";
import TourPage from "./cases/Tour";
import SortablePage from "./cases/Sortable";
import MediaPreviewerTest from "./cases/MediaPreviewer";
import CategoryEnhancerOptionsDialogTest from "./cases/CategoryEnhancerOptionsDialog";
import ResourceFilterPage from "./cases/ResourceFilter";
import PropertiesPage from "./cases/Properties";
import PresetMediaLibraryTemplateBuilderTest from "./cases/PresetMediaLibraryTemplateBuilderTest";
import LongTabsPage from "./cases/LongTabs";
import ReactPlayer from "./cases/ReactPlayer";
import HlsPlayerPage from "./cases/HlsPlayer";
import FileNameModifierTestPage from "./cases/FileNameModifierTest";
import FolderSelectorTest from "./cases/FolderSelectorTest";
import MediaPlayerTest from "./cases/MediaPlayer";
import WindowedMediaPlayerTest from "./cases/WindowedMediaPlayer";
import AfterFirstPlayOperationsModalTest from "./cases/AfterFirstPlayOperationsModalTest";
import PathMarksInvalidPathsTest from "./cases/PathMarksInvalidPathsTest";
import BulkModificationProcessEditorsTest from "./cases/BulkModificationProcessEditors";
import ComparisonRuleTest from "./cases/ComparisonRuleTest";
import PropertyValueRendererTestPage from "./cases/PropertyValueRenderer";

import ErrorBoundaryTestPage from "@/pages/test/cases/ErrorBoundaryTest";
import { Listbox } from "@/components/bakaui";
import SimpleLabel from "@/components/SimpleLabel";
import AntdMenu from "@/layouts/BasicLayout/components/PageNav/components/AntdMenu";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import OrderSelector from "@/pages/resource/components/FilterPanel/OrderSelector";
import VirtualListPage from "@/pages/test/cases/VirtualList";
import ResourceTransferPage from "@/pages/test/cases/ResourceTransfer";
import { ProcessValueEditor } from "@/pages/bulk-modification/components/BulkModification/ProcessValue";
import { PropertyType, StandardValueType } from "@/sdk/constants";
import PropertyMatcher from "@/components/PropertyMatcher";
import BetaChip from "@/components/Chips/BetaChip";
import DeprecatedChip from "@/components/Chips/DeprecatedChip";
import { FileSystemSelectorButton } from "@/components/FileSystemSelector";

const components = {
  PropertyValueRenderer: <PropertyValueRendererTestPage />,
  ComparisonRule: <ComparisonRuleTest />,
  BulkModificationProcessEditors: <BulkModificationProcessEditorsTest />,
  PathMarksInvalidPaths: <PathMarksInvalidPathsTest />,
  AfterFirstPlayOperationsModalTest: <AfterFirstPlayOperationsModalTest />,
  FolderSelectorTest: <FolderSelectorTest />,
  WindowedMediaPlayer: <WindowedMediaPlayerTest />,
  ErrorBoundaryTest: <ErrorBoundaryTestPage />,
  FileNameModifierTest: <FileNameModifierTestPage />,
  PropertyMatcher: (
    <PropertyMatcher name={"封面3"} type={PropertyType.Attachment} onValueChanged={console.log} />
  ),
  BetaChip: (
    <div className={"flex flex-wrap gap-2 items-center"}>
      <BetaChip />
      <BetaChip color="primary" size="md" />
      <BetaChip color="success" size="lg" variant="solid" />
      <BetaChip color="danger" tooltipContent="Custom tooltip content" variant="bordered" />
      <BetaChip showTooltip={false} />
    </div>
  ),
  DeprecatedChip: (
    <div className={"flex flex-wrap gap-2 items-center"}>
      <DeprecatedChip />
      <DeprecatedChip color="warning" size="md" />
      <DeprecatedChip color="danger" size="lg" variant="solid" />
      <DeprecatedChip
        color="secondary"
        tooltipContent="Custom deprecated message"
        variant="bordered"
      />
      <DeprecatedChip showTooltip={false} />
    </div>
  ),
  PresetMediaLibraryTemplateBuilder: <PresetMediaLibraryTemplateBuilderTest />,
  Properties: <PropertiesPage />,
  BulkModification: <ProcessValueEditor valueType={StandardValueType.Boolean} />,
  ResourceTransfer: <ResourceTransferPage />,
  ResourceFilter: <ResourceFilterPage />,
  VirtualList: <VirtualListPage />,
  CategoryEnhancerOptions: <CategoryEnhancerOptionsDialogTest />,
  Psc: <PscPage />,
  Tour: <TourPage />,
  ResourceOrderSelector: <OrderSelector />,
  Menu: <AntdMenu />,
  Sortable: <SortablePage />,
  LongTabs: <LongTabsPage />,
  FileSelector: (
    <FileSystemSelectorButton
      fileSystemSelectorProps={{
        targetType: "file",
        defaultSelectedPath: "I:\\Test",
      }}
    />
  ),
  FolderSelector: (
    <FileSystemSelectorButton defaultSelectedPath={"I:\\Test"} targetType={"folder"} />
  ),
  SimpleLabel: ["dark", "light"].map((t) => {
    return (
      <div
        className={`iw-theme-${t}`}
        style={{
          background: "var(--theme-body-background)",
          padding: 10,
        }}
      >
        {["default", "primary", "success", "warning", "info", "danger"].map((s) => {
          return <SimpleLabel status={s}>{s}</SimpleLabel>;
        })}
      </div>
    );
  }),
  HlsPlayer: (
    <HlsPlayerPage
      src={
        "http://localhost:5000/file/play?fullname=Z%3A%5CAnime%5CAdded%20recently%5CArcane%20S01%5CS01E01%20-%20Welcome%20to%20the%20Playground.mkv"
      }
    />
  ),
  ReactPlayer: <ReactPlayer />,
  MediaPreviewer: <MediaPreviewerTest />,
  MediaPlayer: <MediaPlayerTest />,
};
const SIDEBAR_COLLAPSED_KEY = "test-page-sidebar-collapsed";

const TestPage = () => {
  const { t } = useTranslation();

  const { createPortal } = useBakabaseContext();
  const [testingKey, setTestingKey] = useCookie("test-component-key");
  const [isCollapsed, setIsCollapsed] = useLocalStorage(SIDEBAR_COLLAPSED_KEY, false);

  useEffect(() => {
    if (testingKey == null) {
      setTestingKey(Object.keys(components)[0]);
    }
  }, []);

  const toggleCollapsed = () => {
    setIsCollapsed(!isCollapsed);
  };

  return (
    <div>
      <div className={"flex items-start gap-2 max-h-full h-full"}>
        <div
          className={
            "border-small rounded-small border-default-200 dark:border-default-100 flex flex-col transition-all duration-200"
          }
          style={{ minWidth: isCollapsed ? "auto" : "200px" }}
        >
          <Button
            isIconOnly
            className="self-end m-1"
            size="sm"
            variant="light"
            onPress={toggleCollapsed}
          >
            {isCollapsed ? <ChevronRightIcon /> : <ChevronLeftIcon />}
          </Button>
          {!isCollapsed && (
            <div className="px-1 pb-2">
              <Listbox
                selectedKeys={testingKey ? [testingKey] : undefined}
                onAction={(k) => {
                  setTestingKey(k as string);
                }}
              >
                {Object.keys(components).sort().map((c) => {
                  return <ListboxItem key={c}>{c}</ListboxItem>;
                })}
              </Listbox>
            </div>
          )}
        </div>
        <div className={"flex flex-col gap-2 grow max-h-full h-full overflow-auto"}>
          <div className={""} id={testingKey}>
            {components[testingKey]}
          </div>
        </div>
      </div>
    </div>
  );
};

TestPage.displayName = "TestPage";

// Render the form with all the properties we just defined passed
// as props
export default TestPage;
