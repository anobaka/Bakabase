import { useState, useCallback } from "react";
import { useTranslation } from "react-i18next";
import { AiOutlineWarning } from "react-icons/ai";

import PathTree from "@/pages/path-marks/components/PathTree";
import type { PathMarkGroup } from "@/pages/path-mark-config/hooks/usePathMarks";
import type { BakabaseAbstractionsModelsDomainPathMark } from "@/sdk/Api";
import { PathMarkType } from "@/sdk/constants";
import { Switch, toast } from "@/components/bakaui";

// Helper to create mock marks with required fields
const createMockMark = (
  id: number,
  path: string,
  type: number,
  priority: number
): BakabaseAbstractionsModelsDomainPathMark => ({
  id,
  path,
  type: type as BakabaseAbstractionsModelsDomainPathMark["type"],
  priority,
  syncStatus: 0 as BakabaseAbstractionsModelsDomainPathMark["syncStatus"],
  configJson: "",
  createdAt: new Date().toISOString(),
  updatedAt: new Date().toISOString(),
  isDeleted: false,
});

// Mock data with complex nested valid/invalid paths
const mockGroups: PathMarkGroup[] = [
  // === C: Drive - Mixed valid/invalid in nested structure ===
  // Valid parent
  {
    path: "C:/Users/Demo/Videos/Anime",
    marks: [createMockMark(1, "C:/Users/Demo/Videos/Anime", PathMarkType.Resource, 1)],
    exists: true,
  },
  // Valid child under valid parent
  {
    path: "C:/Users/Demo/Videos/Anime/Subdir",
    marks: [createMockMark(6, "C:/Users/Demo/Videos/Anime/Subdir", PathMarkType.MediaLibrary, 10)],
    exists: true,
  },
  // INVALID child under valid parent (deleted subfolder)
  {
    path: "C:/Users/Demo/Videos/Anime/DeletedSeason",
    marks: [createMockMark(8, "C:/Users/Demo/Videos/Anime/DeletedSeason", PathMarkType.Resource, 1)],
    exists: false,
  },
  // Valid sibling
  {
    path: "C:/Users/Demo/Videos/Movies",
    marks: [
      createMockMark(2, "C:/Users/Demo/Videos/Movies", PathMarkType.Resource, 1),
      createMockMark(3, "C:/Users/Demo/Videos/Movies", PathMarkType.Property, 100),
    ],
    exists: true,
  },
  // INVALID sibling (deleted folder at same level)
  {
    path: "C:/Users/Demo/Videos/DeletedCategory",
    marks: [createMockMark(9, "C:/Users/Demo/Videos/DeletedCategory", PathMarkType.Resource, 1)],
    exists: false,
  },
  // Deep nested: valid -> valid -> INVALID
  {
    path: "C:/Users/Demo/Videos/Movies/Action",
    marks: [createMockMark(10, "C:/Users/Demo/Videos/Movies/Action", PathMarkType.Resource, 1)],
    exists: true,
  },
  {
    path: "C:/Users/Demo/Videos/Movies/Action/2024",
    marks: [createMockMark(11, "C:/Users/Demo/Videos/Movies/Action/2024", PathMarkType.Resource, 1)],
    exists: true,
  },
  {
    path: "C:/Users/Demo/Videos/Movies/Action/2024/Deleted",
    marks: [createMockMark(12, "C:/Users/Demo/Videos/Movies/Action/2024/Deleted", PathMarkType.Resource, 1)],
    exists: false,
  },
  // Valid after invalid in same branch
  {
    path: "C:/Users/Demo/Videos/Movies/Action/2023",
    marks: [createMockMark(13, "C:/Users/Demo/Videos/Movies/Action/2023", PathMarkType.Resource, 1)],
    exists: true,
  },

  // === D: Drive - Single invalid path ===
  {
    path: "D:/OldMedia/DeletedFolder",
    marks: [createMockMark(4, "D:/OldMedia/DeletedFolder", PathMarkType.Resource, 1)],
    exists: false,
  },

  // === E: Drive - External drive not connected ===
  {
    path: "E:/ExternalDrive/NotConnected",
    marks: [createMockMark(5, "E:/ExternalDrive/NotConnected", PathMarkType.Property, 50)],
    exists: false,
  },
  // Another path on same disconnected drive
  {
    path: "E:/ExternalDrive/AnotherFolder",
    marks: [createMockMark(14, "E:/ExternalDrive/AnotherFolder", PathMarkType.Resource, 1)],
    exists: false,
  },

  // === F: Drive - Removed partition ===
  {
    path: "F:/RemovedPartition/Data",
    marks: [createMockMark(7, "F:/RemovedPartition/Data", PathMarkType.Resource, 1)],
    exists: false,
  },

  // === H: Drive - Invalid nested in invalid ===
  // Parent invalid
  {
    path: "H:/DeletedDrive/Folder1",
    marks: [createMockMark(18, "H:/DeletedDrive/Folder1", PathMarkType.Resource, 1)],
    exists: false,
  },
  // Child also invalid (parent folder was deleted)
  {
    path: "H:/DeletedDrive/Folder1/SubFolder",
    marks: [createMockMark(19, "H:/DeletedDrive/Folder1/SubFolder", PathMarkType.Resource, 1)],
    exists: false,
  },
  // Deeper child also invalid
  {
    path: "H:/DeletedDrive/Folder1/SubFolder/DeepFolder",
    marks: [createMockMark(20, "H:/DeletedDrive/Folder1/SubFolder/DeepFolder", PathMarkType.Resource, 1)],
    exists: false,
  },
  // Another branch, also all invalid
  {
    path: "H:/DeletedDrive/Folder2",
    marks: [createMockMark(21, "H:/DeletedDrive/Folder2", PathMarkType.Resource, 1)],
    exists: false,
  },
  {
    path: "H:/DeletedDrive/Folder2/Data",
    marks: [createMockMark(22, "H:/DeletedDrive/Folder2/Data", PathMarkType.Property, 50)],
    exists: false,
  },

  // === G: Drive - All valid for comparison ===
  {
    path: "G:/Backup/Media",
    marks: [createMockMark(15, "G:/Backup/Media", PathMarkType.Resource, 1)],
    exists: true,
  },
  {
    path: "G:/Backup/Media/Photos",
    marks: [createMockMark(16, "G:/Backup/Media/Photos", PathMarkType.Resource, 1)],
    exists: true,
  },
  {
    path: "G:/Backup/Media/Videos",
    marks: [createMockMark(17, "G:/Backup/Media/Videos", PathMarkType.Resource, 1)],
    exists: true,
  },
];

const PathMarksInvalidPathsTest = () => {
  const { t } = useTranslation();
  const [showOnlyInvalid, setShowOnlyInvalid] = useState(false);

  const invalidPathsCount = mockGroups.filter(g => g.exists === false).length;
  const filteredGroups = showOnlyInvalid
    ? mockGroups.filter(g => g.exists === false)
    : mockGroups;

  const handleConfigureSubPathMarks = useCallback(
    (path: string) => {
      toast.success(`Configure sub-path marks: ${path} (In production this would open PathConfigModal)`);
      console.log("Configure sub-path marks", path);
    },
    [],
  );

  return (
    <div className="path-marks-page h-full flex flex-col">
      <div className="flex flex-col gap-4 p-4 flex-1 min-h-0">
        {/* Header */}
        <div className="flex items-center justify-between">
          <div className="flex items-center gap-2">
            <h2 className="text-xl font-semibold">{t("pathMarks.title")} - Mock Test</h2>
          </div>
        </div>

        {/* Description */}
        <div className="text-sm text-default-500">
          This is a mock test showing how invalid paths are displayed.
          Total {mockGroups.length} paths, {invalidPathsCount} invalid.
        </div>

        {/* Invalid paths warning */}
        {invalidPathsCount > 0 && (
          <div className="flex items-center gap-3 p-3 bg-warning-50 border border-warning-200 rounded-lg">
            <AiOutlineWarning className="text-warning text-xl flex-shrink-0" />
            <div className="flex-1">
              <span className="text-warning-700">
                {t("{{count}} path(s) no longer exist on the file system", { count: invalidPathsCount })}
              </span>
            </div>
            <div className="flex items-center gap-2">
              <span className="text-sm text-default-600">{t("pathMarks.label.showOnlyInvalid")}</span>
              <Switch
                isSelected={showOnlyInvalid}
                size="sm"
                onValueChange={setShowOnlyInvalid}
              />
            </div>
          </div>
        )}

        {/* Content */}
        <div className="overflow-auto flex-1 min-h-0 border border-default-200 rounded-lg p-2">
          <PathTree
            groups={filteredGroups}
            onConfigureSubPathMarks={handleConfigureSubPathMarks}
            onDeleteMark={(path, mark) => {
              toast.success(`Deleted mark ${mark.id} from ${path} (mock)`);
              console.log("Delete mark", path, mark);
            }}
            onDeletePathMarks={(path) => {
              toast.success(`Deleted all marks from ${path} (mock)`);
              console.log("Delete path marks", path);
            }}
            onSaveMark={(path, mark, oldMark) => {
              toast.success(`Saved mark to ${path} (mock)`);
              console.log("Save mark", path, mark, oldMark);
            }}
            onPasteMarks={(path, marks) => {
              toast.success(`Pasted ${marks.length} marks to ${path} (mock)`);
              console.log("Paste marks", path, marks);
            }}
          />
        </div>
      </div>
    </div>
  );
};

export default PathMarksInvalidPathsTest;
