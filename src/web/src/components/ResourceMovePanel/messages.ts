import { useTranslation } from "react-i18next";

/** Component-local messages keep this new feature independent of generated SDK/locales. */
const messages = {
  restoreSource: ["Confirm source restored…", "确认已恢复原路径…"],
  restoreSourceTitle: [
    "Confirm complete resource restored to source",
    "确认完整资源已恢复到原路径",
  ],
  restoreSourceHelp: [
    "First manually restore the complete resource, including every child file and folder, to the original source path below. This action only verifies the source, cancels this recovery record, and releases its reservation; it does not move files or change the destination.",
    "请先手工将完整资源（包括全部子文件和子目录）恢复到下方的原路径。此操作只校验源位置、取消这条恢复记录并解除占用，不会移动文件或修改目标位置。",
  ],
  restoreSourceAcknowledgement: [
    "I have manually restored the complete resource to this original source path.",
    "我已手工将完整资源恢复到此原路径。",
  ],
  restoreSourceSubmit: ["Verify restored source and release reservation", "校验原路径并解除占用"],
  restorePlatformSource: [
    "Confirm source and platform links restored…",
    "确认源文件及平台关联已恢复…",
  ],
  restorePlatformSourceTitle: [
    "Confirm source files and platform links restored",
    "确认源文件及平台关联已恢复",
  ],
  restorePlatformSourceHelp: [
    "Manually restore the complete resource, including every child file and folder, to the original source path below. Also verify and restore its platform source links to that same location; copying the folder alone is not enough. The server will verify the restored files and source links, then cancel this record and release its reservation. Destination and temporary move files are kept.",
    "请手工将完整资源（包括全部子文件和子目录）恢复到下方原路径，同时核对并将平台来源关联恢复到同一位置；仅复制目录并不足够。服务端会校验恢复后的文件和来源关联，再取消此记录并解除占用。目标位置及临时移动文件会保留。",
  ],
  restorePlatformSourceAcknowledgement: [
    "I have restored the complete resource to its original path and verified that its platform source links point to that location.",
    "我已将完整资源恢复到原路径，并核对平台来源关联已指向该位置。",
  ],
  restorePlatformSourceSubmit: [
    "Verify restored source and platform links, then release reservation",
    "校验源文件及平台关联并解除占用",
  ],

  hideForNow: ["Hide for now; keep checking later", "暂时收起，稍后确认结果"],
  unresolved: ["Unconfirmed submissions", "提交结果待确认"],
  title: ["Move resources", "移动资源"],
  paths: ["Destinations", "目标路径"],
  tasks: ["Move tasks", "移动任务"],
  add: ["Add destination", "添加目标"],
  edit: ["Edit destination", "编辑目标"],
  remove: ["Remove from panel", "从面板移除"],
  undo: ["Undo remove", "撤销移除"],
  global: ["All resource tabs", "所有资源 Tab"],
  local: ["Current tab", "当前 Tab"],
  globalDestination: ["Use in all resource tabs", "设为全局路径（所有资源 Tab 可用）"],
  pinGlobal: ["Pin for all resource tabs", "设为全局路径"],
  unpinGlobal: ["Use only in the current tab", "取消全局，仅在当前 Tab 使用"],
  noPaths: [
    "Save a folder, then drop selected resources onto it.",
    "保存目标文件夹，然后将选中的资源拖到这里。",
  ],
  selected: ["selected", "项已选择"],
  moveSelected: ["Move selected", "移动所选"],
  openFolder: ["Open folder on server", "在服务器打开文件夹"],
  copyPath: ["Copy full path", "复制完整路径"],
  noLibrary: ["No associated media library", "无关联媒体库"],
  related: [
    "Related media libraries; actual effects are shown in the preview.",
    "相关媒体库；实际影响以本次预览为准。",
  ],
  name: ["Display name (optional)", "显示名称（可选）"],
  path: ["Destination folder", "目标文件夹"],
  scope: ["Available in", "可用范围"],
  save: ["Save", "保存"],
  cancel: ["Cancel", "取消"],
  close: ["Close panel (tasks continue)", "关闭面板（任务继续）"],
  minimize: ["Minimize", "最小化"],
  expand: ["Expand move panel", "展开移动面板"],
  dock: ["Dock to right", "停靠右侧"],
  float: ["Float panel", "切换为浮窗"],
  grouped: ["Group shared prefixes", "聚合共同路径前缀"],
  flat: ["Flat list", "平铺路径"],
  sort: ["Drag to reorder within this scope", "拖动可在当前范围内排序"],
  up: ["Move up", "上移"],
  down: ["Move down", "下移"],
  unavailable: ["Folder unavailable", "目录不可用"],
  duplicate: ["This destination is already saved in this scope.", "当前范围内已经保存了这个目标。"],
  folderRequired: ["Choose an existing folder.", "请选择实际存在的文件夹。"],
  prepare: ["Preparing preview…", "正在准备预览…"],
  confirm: ["Confirm resource move", "确认移动资源"],
  confirmMove: ["Confirm move", "确认移动"],
  previewFailed: ["Preview failed. Cancel and try again.", "预览失败，请取消后重试。"],
  physical: [
    "Files will move on disk. Canceling a running task keeps completed moves.",
    "文件将发生实际移动。取消运行中的任务会保留已经完成的移动。",
  ],
  affected: ["Also affects child resources", "同时影响子资源"],
  excluded: ["Excluded from this move", "本次不会移动"],
  noMovableResources: [
    "None of the selected resources can be moved. Review the reasons below.",
    "所选资源均不可移动，请查看下方原因。",
  ],
  blockingResources: ["Blocking resources", "阻止本次移动的资源"],
  unselectedSteamChild: ["Steam-managed child resource", "由 Steam 管理的子资源"],
  resource: ["Resource", "资源"],
  count: ["Resources", "资源"],
  topLevel: ["Top-level moves", "实际顶层移动"],
  conflict: ["Name conflict", "同名冲突"],
  invalidTarget: ["Destination is inside a source folder", "目标位于源目录中"],
  waiting: ["Waiting for a decision", "等待处理"],
  unknown: ["Checking submission result", "正在确认提交结果"],
  unknownHelp: [
    "The server may have accepted this batch. Resources remain reserved. Retry safely using the same request ID.",
    "服务端可能已经接受本批任务，资源保持占用。请使用相同请求标识安全重试。",
  ],
  retrySubmission: ["Check / retry submission", "确认结果 / 安全重试"],
  submitting: ["Submitting…", "正在提交…"],
  noTasks: ["No move tasks yet", "暂无移动任务"],
  allTasks: ["All tabs", "所有 Tab"],
  currentTasks: ["This tab", "当前 Tab"],
  autoOverwrite: ["Automatically overwrite conflicts for this panel", "本面板自动覆盖同名文件"],
  autoHelp: [
    "Applies to waiting and future panel tasks that inherit this policy. Each move still needs confirmation. Files of different types and resource identity conflicts are excluded.",
    "作用于继承此策略的当前等待任务与后续面板任务。每批移动仍需确认，不包括文件类型冲突与资源身份冲突。",
  ],
  policy: ["This batch's conflict policy", "本批同名冲突策略"],
  inherit: ["Use panel setting", "继承面板设置"],
  ask: ["Always ask", "始终询问"],
  overwrite: ["Overwrite same-name files", "覆盖同名文件"],
  once: ["This conflict only", "仅当前冲突"],
  batch: ["Remaining conflicts in this batch", "当前任务的剩余冲突"],
  panel: ["All panel tasks", "本面板所有任务"],
  resolveOverwrite: ["Overwrite and continue", "覆盖并继续"],
  skip: ["Skip resource", "跳过此资源"],
  stop: ["Stop remaining moves", "停止未完成部分"],
  stoppingHelp: ["Finishing the current resource before stopping", "完成当前资源后停止"],
  retry: ["Retry eligible items", "重试可恢复项目"],
  refresh: ["Refresh", "刷新"],
  more: ["Load older tasks", "加载更早任务"],
  queued: ["Queued", "排队中"],
  running: ["Moving", "移动中"],
  stopping: ["Stopping", "正在停止"],
  completed: ["Completed", "已完成"],
  partial: ["Partially completed", "部分完成"],
  failed: ["Failed", "失败"],
  cancelled: ["Cancelled", "已取消"],
  needsRecovery: ["Recovery required", "需要恢复"],
  succeeded: ["Moved", "成功"],
  skipped: ["Skipped", "跳过"],
  pendingOther: ["Other tabs need attention", "其他 Tab 有待处理任务"],
  idle: ["Idle", "空闲"],
  details: ["Details", "详情"],
  choose: ["Browse folders", "浏览文件夹"],
  scopeHint: [
    "Outside the resource page, only global destinations are available.",
    "资源页之外仅使用所有 Tab 可用的路径。",
  ],
  copyFailed: ["Could not copy path", "复制路径失败"],
  blocked: [
    "Some resources are already reserved. Cancel this preview and select movable resources.",
    "部分资源已被占用，请取消预览并选择可移动资源。",
  ],
  serverScope: ["Server folder", "服务器目录"],
  taskSource: ["Source", "来源"],
  noTab: ["Outside resource tabs", "资源 Tab 外"],
} as const;

export type MoveMessage = keyof typeof messages;
export function useMoveText() {
  const { i18n } = useTranslation();

  return (key: MoveMessage) => messages[key][/^(zh|cn)/i.test(i18n.language ?? "en") ? 1 : 0];
}

const moveReasons = {
  sourceContextRequired: [
    "The source library could not be verified. Reload the library before starting a move. Check the task list before handling an older unconfirmed submission.",
    "无法确认来源媒体库，请重新加载资源页后再移动。处理旧的待确认提交前，请先检查任务列表。",
  ],
  foreignMoveSource: [
    "These resources belong to another device. Open that device's management view to move its files; resources from shared libraries cannot be moved here.",
    "这些资源属于另一台设备。请打开该设备的管理视图移动文件；共享媒体库中的资源不能在此移动。",
  ],
  sourceContextChanged: [
    "The library at this address changed. Reload before starting another move. Return to the original library to check an unconfirmed submission.",
    "此地址的媒体库已变更，请重新加载后再开始移动。待确认的提交需要返回原媒体库检查。",
  ],
  invalidMoveSourceReferences: [
    "The selected resources do not match their source references. Reload the library and select them again.",
    "所选资源与来源标识不一致，请重新加载媒体库并重新选择。",
  ],
  steamManaged: [
    "The source or destination is managed by Steam. Move installations using Steam's storage settings.",
    "源位置或目标位置由 Steam 管理，请在 Steam 客户端的存储设置中移动游戏安装目录。",
  ],
  containsSteamManagedResource: [
    "This folder contains Steam-managed resources. The entire folder cannot be moved here; move those installations using Steam first.",
    "此文件夹包含由 Steam 管理的资源，不能整体移动；请先在 Steam 中处理这些安装位置。",
  ],
  sourceMoveUnsupported: [
    "This resource's source does not support moving its local files yet.",
    "此资源的来源暂不支持移动本地文件。",
  ],
  sourceRecordMissing: [
    "The platform source record is missing. Check and restore the source record before retrying.",
    "平台来源记录缺失，请核对并恢复对应的来源记录后重试。",
  ],
  sourceLocationChanged: [
    "The resource path or source links changed after confirmation. Preview again if the move has not started; for a recovery task, restore the expected location or source links before retrying.",
    "资源路径或来源关联与确认时不一致。尚未开始的移动请重新预览；恢复中的任务请先还原位置或来源关联后重试。",
  ],
  legacySourcePlanMissing: [
    "This move began before the upgrade and has no saved plan for updating its platform source links. The resource remains reserved. Manually check the original and destination files and their platform source links before recovery. Retrying cannot rebuild the missing plan.",
    "此移动在升级前已开始，但缺少用于修正平台来源关联的计划快照，资源仍保持占用。请人工核对原位置、目标位置的文件及平台来源关联，再处理恢复；重试不能补回缺失的计划。",
  ],
  noLocalFiles: [
    "This resource has no local files to move. Download or install it first.",
    "此资源尚无可移动的本地文件，请先下载或安装。",
  ],
  resourceLocked: [
    "Another move is using this resource. Wait for it to finish or cancel that task.",
    "此资源正被其他移动任务占用，请等待任务结束或取消该任务。",
  ],
  destinationMissing: [
    "The destination folder is unavailable. Check that the drive is connected and the folder exists.",
    "目标文件夹不可用，请检查磁盘连接及目录是否存在。",
  ],
  sourceMissing: [
    "The local source file or folder could not be found. Check its location and drive connection.",
    "找不到本地源文件或文件夹，请检查资源位置和磁盘连接。",
  ],
  destinationInsideSource: [
    "The destination is inside the source folder. Choose a different destination.",
    "目标位于源目录内部，请选择其他目标位置。",
  ],
  alreadyAtDestination: [
    "This resource is already in the selected destination folder.",
    "此资源已经位于所选目标文件夹中。",
  ],
  unavailable: [
    "This resource cannot be moved. Refresh it and check the source and destination.",
    "此资源当前无法移动，请刷新资源并检查源位置与目标位置。",
  ],
} as const;

export type MoveReasonCode = keyof typeof moveReasons;
export function getKnownMoveReasonCode(reason?: string | null): MoveReasonCode | undefined {
  const code = reason?.trim().split(":", 1)[0];

  return code && Object.prototype.hasOwnProperty.call(moveReasons, code)
    ? (code as MoveReasonCode)
    : undefined;
}
export function getMoveReasonText(reasonCode?: string | null, language = "en"): string {
  const code = getKnownMoveReasonCode(reasonCode) ?? "unavailable";

  return moveReasons[code][/^(zh|cn)/i.test(language) ? 1 : 0];
}
export function useMoveReasonText() {
  const { i18n } = useTranslation();

  return (reasonCode?: string | null) => getMoveReasonText(reasonCode, i18n.language);
}
