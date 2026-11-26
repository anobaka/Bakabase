// Inlined into both maintenance pages. They also run before the main app is available.
const setupI18n = (() => {
  const preferenceKey = 'bakabase.setup.language';

  function normalize(language) {
    const base = language?.toLowerCase().split(/[-_]/)[0];
    if (base === 'cn' || base === 'zh') return 'cn';
    if (base === 'en') return 'en';
  }

  function resolve(explicit, previousSession) {
    let preference;
    try { preference = localStorage.getItem(preferenceKey); } catch {}
    const candidates = [explicit, preference, previousSession, ...(navigator.languages || []), navigator.language];
    return candidates.map(normalize).find(Boolean) || 'en';
  }

  function remember(language) {
    try { localStorage.setItem(preferenceKey, language); } catch {}
  }

  // The maintenance API and saved progress snapshots carry English diagnostics.
  // Match known wire messages and keep any unfamiliar system or filesystem detail verbatim.
  const diagnostics = new Map([
    ['Use an absolute directory path.', '请输入目录的绝对路径。'],
    ['Use an absolute directory path on the server.', '请输入服务端可访问的目录绝对路径。'],
    ['Choose a dedicated data directory, not a filesystem root.', '请选择专用的数据文件夹，不能使用文件系统根目录。'],
    ['The selected data path is a file.', '所选路径是文件。请选择文件夹。'],
    ['Choose an empty destination directory. To import a library, select its directory as the source instead.', '请选择空的目标目录。要导入已有媒体库，请将其目录选为来源。'],
    ['The destination must be empty. Existing files will not be merged or overwritten.', '目标目录必须为空。请选择空文件夹，以保留已有文件。'],
    ['Choose an empty data directory, not a file or filesystem root.', '请选择空的数据文件夹，不能使用文件或文件系统根目录。'],
    ['The original appdata path must be an absolute directory, not a drive root.', '原设备上的数据目录必须是绝对路径，不能是驱动器根目录。'],
    ['The source directory does not exist on the server.', '服务端找不到来源目录，请检查路径和挂载。'],
    ['The source must contain app.json and bakabase_insideworld.db.', '来源目录中必须同时包含 app.json 和 bakabase_insideworld.db。'],
    ['The source app.json has no valid version; its upgrade path cannot be determined.', '来源 app.json 中的版本无效，无法确定如何升级。'],
    ['The source has an unfinished data move/import. Complete or cancel it first.', '来源实例还有未完成的数据迁移或导入。请先完成或取消该操作。'],
    ['Source and destination must be separate directories, without containment.', '来源目录和目标目录必须不同，且不能互相包含。'],
    ['Source and destination must be different, non-nested data directories.', '来源目录和目标目录必须不同，且不能互相包含。'],
    ['The import source, current library and destination must be separate, non-nested directories.', '导入来源、当前媒体库和目标必须是三个独立的目录，且不能互相包含。'],
    ['The destination cannot contain the application\'s data anchor.', '目标目录不能包含应用的数据入口。'],
    ['The source is in use or aliases this server\'s data directory. Stop the old instance and use separate source and destination directories.', '来源正在使用，或指向当前服务的数据目录。请先关闭旧实例并选择独立的来源和目标目录。'],
    ['The current data directory does not contain an initialized library.', '当前数据目录中没有已初始化的媒体库。'],
    ['This directory belongs to another registered workspace. Choose a separate directory.', '此目录属于另一个已注册的工作区，请选择独立的目录。'],
    ['The launch-control directory cannot be used as a data directory.', '请选择启动控制目录以外的数据目录。'],
    ['Choose a data directory outside the application installation.', '请选择应用安装位置以外的数据目录。'],
    ['A workspace cannot be placed inside an existing data directory.', '请选择已有数据目录以外的位置保存工作区。'],
    ['Complete or cancel the existing data operation before moving this library.', '请先完成或取消已有的数据操作，再迁移媒体库。'],
    ['This server\'s data path is fixed by its deployment. Change the deployment configuration to choose another path.', '保存位置由部署配置固定。如需更换，请修改部署配置。'],
    ['This folder no longer exists or its drive is unavailable. Choose another folder.', '此文件夹已不存在或驱动器不可用，请选择其他文件夹。'],
    ['The server does not have permission to read this folder. Choose another folder or adjust its access permissions.', '服务端没有权限读取此文件夹。请选择其他文件夹或调整访问权限。'],
    ['The directory path is too long (maximum 4096 characters).', '目录路径过长，最多支持 4096 个字符。'],
    ['Use one folder name, up to 255 characters, without slashes, leading or trailing spaces, or parent-directory notation.', '请输入单个文件夹名称，最多 255 个字符。名称不能包含斜杠、首尾空格，也不能是 . 或 ..。'],
    ['This folder name is reserved by Windows. Choose a different name.', '这是 Windows 保留名称，请使用其他文件夹名称。'],
    ['The saved setup draft is too large. Clear it to start again.', '已保存的草稿过大，请清空后重新设置。'],
    ['The saved setup draft is invalid. Clear it to start again.', '已保存的草稿无效，请清空后重新设置。'],
    ['The setup draft is too large.', '设置草稿过大。'],
    ['A draft supports up to 256 path mappings.', '一份草稿最多支持 256 条路径映射。'],
    ['Use at most 256 path mappings.', '最多支持 256 条路径映射。'],
    ['Mapping prefixes must be absolute Windows, UNC or POSIX paths.', '映射前缀必须是 Windows、UNC 或 POSIX 绝对路径。'],
    ['Duplicate mapping source prefix.', '有两条映射使用了相同的来源前缀，请保留一条。'],
    ['Mapping target prefixes collide.', '映射的目标前缀发生冲突，请检查目标目录。'],
    ['Mapping rules contain a cycle or overlapping source and destination.', '映射规则存在循环，或来源与目标重叠。'],
    ['Mapping would merge distinct stored paths into the same destination.', '多个原路径会映射到同一目标，请调整规则。'],
    ['Path preview requires stopped, checkpointed databases; a WAL still contains data.', '来源数据库仍有未写回的数据。请正常关闭来源实例，再重新扫描。'],
    ['The source changed during preflight. Stop its Bakabase instance and scan again.', '预检期间来源发生变化。请关闭使用来源的 Bakabase 实例，再重新扫描。'],
    ['The source changed after preflight. Scan it again before importing.', '预检后来源发生变化，请在导入前重新扫描。'],
    ['The import selection or mappings changed. Review the preflight again before importing.', '导入目录或路径映射已更改，请重新预检后再导入。'],
    ['The path scan changed. Preview the mappings again.', '路径扫描结果已更改，请重新预览映射。'],
    ['Unmapped references will keep their original paths. Their files may be unavailable on this device.', '未匹配替换规则的引用会保留原路径，这些文件在当前设备上可能无法访问。'],
    ['Destination availability is not checked here. Confirm the server/container mounts using the folder browser; no media files will be moved or folders created.', '这里不会检查目标目录是否可用。请通过文件夹浏览器确认服务端或容器中的挂载位置；不会搬移媒体文件或创建文件夹。'],
    ['Complete the import preflight before reviewing or applying path mappings.', '请先完成导入预检，再查看或应用路径映射。'],
    ['The source explicitly disables remote access, which would block the Docker browser after import. Import a separate copy into the native server first and choose the remote access/pairing settings you need, then stop it and import that prepared copy into Docker. The original source has not been changed.', '来源已关闭远程访问，直接导入会导致浏览器无法访问 Docker 中的服务。请先在原生服务端导入一份副本，设置远程访问和配对，正常关闭后再将副本导入 Docker。原始来源保持不变。'],
  ]);

  // A few diagnostics include a server path or version. Only translate the fixed part.
  const withPath = [
    ['Choose a folder inside a mounted storage location. ', '请选择已挂载存储中的文件夹。当前路径：'],
    ['Reconnect the mounted storage location. ', '请重新连接已挂载的存储：'],
    ['Select the actual data directory, or mount it in the container: ', '请选择实际的数据目录，或将它挂载到容器中：'],
    ['Symbolic links are not supported during appdata import: ', '导入数据时无法读取符号链接：'],
    ['Symbolic links are not supported during AppData relocation: ', '迁移数据时无法复制符号链接：'],
    ['SQLite integrity check failed: ', 'SQLite 完整性检查失败：'],
  ];

  function message(detail, language) {
    if (!detail || language !== 'cn') return detail;
    const translation = diagnostics.get(detail);
    if (translation) return translation;
    for (const [prefix, translated] of withPath) {
      if (detail.startsWith(prefix)) return translated + detail.slice(prefix.length);
    }
    const missingConfig = /^Could not find file '(.+[\\/]app\.json)'\.$/.exec(detail);
    if (missingConfig) return '找不到来源配置文件 ' + missingConfig[1] + '。请选择包含 app.json 和 bakabase_insideworld.db 的数据目录。';
    const versions = /^Source version (\S+) is newer than this server \((\S+)\)\. Upgrade the server first\.$/.exec(detail);
    if (versions) return '来源版本 ' + versions[1] + ' 高于当前服务端版本 ' + versions[2] + '，请先升级服务端。';
    return detail;
  }

  return { resolve, remember, message };
})();
