// ignore: unused_import
import 'package:intl/intl.dart' as intl;
import 'app_localizations.dart';

// ignore_for_file: type=lint

/// The translations for Chinese (`zh`).
class AppLocalizationsZh extends AppLocalizations {
  AppLocalizationsZh([String locale = 'zh']) : super(locale);

  @override
  String get appTitle => 'Bakabase';

  @override
  String get connectTitle => '连接到 Bakabase';

  @override
  String get connecting => '连接中…';

  @override
  String get onThisNetwork => '本网络中';

  @override
  String get discoveryHint =>
      '搜索中… 请确认 Bakabase 正在运行且已开启远程访问，并且本设备与它在同一网络。iOS 首次使用时请允许「本地网络」权限。';

  @override
  String get remembered => '最近连接';

  @override
  String get rememberedEmpty => '连接过的服务器会记在这里。';

  @override
  String get byAddress => '手动输入地址';

  @override
  String get connect => '连接';

  @override
  String couldNotConnect(String url) {
    return '无法连接到 $url';
  }

  @override
  String get remoteAccessDisabledHint =>
      '远程访问未开启。请在主机上的 Bakabase 中开启（设置 → 远程访问）。';

  @override
  String protocolTooNew(String version) {
    return '服务端协议为 v$version，当前 App 版本过旧，请升级 App。';
  }

  @override
  String protocolTooOld(String version) {
    return '服务端协议为 v$version，需要更新主机上的 Bakabase。';
  }

  @override
  String get searchHint => '搜索资源';

  @override
  String get allLibraries => '全部';

  @override
  String get sortTooltip => '排序';

  @override
  String get sortAddDt => '最近添加';

  @override
  String get sortPlayedAt => '最近播放';

  @override
  String get sortFileModifyDt => '文件修改时间';

  @override
  String get sortFilename => '文件名';

  @override
  String get ascending => '升序 ↑';

  @override
  String get descending => '降序 ↓';

  @override
  String get playHistoryTooltip => '播放历史';

  @override
  String get switchServerTooltip => '切换服务器';

  @override
  String get openWebUiTooltip => '打开完整 Web 界面';

  @override
  String get playHere => '在本机播放';

  @override
  String get builtInPlayer => '内置播放器（mpv）';

  @override
  String get copyStreamLink => '复制流地址';

  @override
  String get streamLinkCopied => '流地址已复制，粘贴到任意播放器即可播放';

  @override
  String couldNotOpenPlayer(String name) {
    return '无法打开 $name，请确认已安装';
  }

  @override
  String get otherPlayer => '其他播放器…';

  @override
  String readPages(int count) {
    return '阅读（共 $count 页）';
  }

  @override
  String get playSection => '播放';

  @override
  String get otherFiles => '其他文件';

  @override
  String get noPlayableFiles => '此资源中没有找到可播放的文件。';

  @override
  String get playHistory => '播放历史';

  @override
  String removedResource(int id) {
    return '资源 #$id（已删除）';
  }
}
