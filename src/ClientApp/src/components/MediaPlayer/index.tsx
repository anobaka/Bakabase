import { Drawer, Icon, Progress, Tree } from '@alifd/next';
import React, { useCallback, useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useUpdate, useUpdateEffect } from 'react-use';
import './index.scss';
import ReactPlayer from 'react-player';
import moment from 'moment';
import Queue from 'queue';
import FileSystemEntryIcon from '@/components/FileSystemEntryIcon';
import { IconType, IwFsType, MediaType } from '@/sdk/constants';
import CustomIcon from '@/components/CustomIcon';
import {
  buildLogger,
  createPortalOfComponent,
  forceFocus,
  splitPathIntoSegments,
  useTraceUpdate,
} from '@/components/utils';
import BApi from '@/sdk/BApi';
import BusinessConstants from '@/components/BusinessConstants';
import serverConfig from '@/serverConfig';
import TextReader from '@/components/TextReader';

const style = {
  maxWidth: document.body.clientWidth * 0.9,
  maxHeight: document.body.clientHeight * 0.9,
  width: document.body.clientHeight * 0.9,
  height: document.body.clientHeight * 0.9,
};

const compressedFileExtensions = ['rar', 'zip', '7z'];

interface IPropsFile {
  path: string;
  startTime?: string;
  endTime?: string;
}

interface IProps extends React.HTMLAttributes<HTMLElement> {
  defaultActiveIndex?: number;
  files: IPropsFile[];
  interval?: number;
  afterClose?: () => void;
  autoPlay?: boolean;
  renderOperations?: (filePath: string, mediaType: MediaType, playing: boolean, reactPlayer: ReactPlayer | null, image: HTMLImageElement | null) => any;
}

class Node {
  label: string;
  children: Node[] = [];
  key: string;
  parent?: Node;

  type: 'directory' | 'compressed-file' | 'file' | 'unknown';
  isCompressedFileContent: boolean;
  startSeconds?: number;
  endSeconds?: number;
  /**
   * Compressed file contents will be scanned only once whether it is scanned successfully or not unless we cancelled it manually.
   */
  loadingInfoAsync?: AbortController;

  get playable(): boolean {
    return this.type == 'file' || (this.type == 'compressed-file' && !this.isCompressedFileContent);
  }
}

function compressTree(node: Node) {
  if (node.children.length == 1 && node.children[0].type == 'directory') {
    const child = node.children[0];
    node.type = child.children.length > 0 ? 'directory' : getFileType(child.key);
    node.label = node.label ? `${node.label}${BusinessConstants.pathSeparator}${child.label}` : child.label;
    node.key = child.key;
    node.children = child.children;
    node.startSeconds = child.startSeconds;
    node.endSeconds = child.endSeconds;
    compressTree(node);
  } else {
    if (node.children.length > 1) {
      for (const child of node.children) {
        compressTree(child);
      }
    }
  }
}

const buildNodeKey = (path: string, compressedFileKey?: string): string => {
  return compressedFileKey ? `${compressedFileKey}!${path}` : path;
};

function buildNodeMap(nodes: Node[]): Record<string, Node> {
  const data: Record<string, Node> = {};
  for (const node of nodes) {
    data[node.key] = node;
    if (node.children.length > 0) {
      Object.assign(data, buildNodeMap(node.children));
    }
  }
  return data;
}

function getFileType(path: string) {
  const ext = path.split('.').pop();
  if (ext) {
    if (compressedFileExtensions.includes(ext)) {
      return 'compressed-file';
    }
  }
  return 'file';
}

function buildBranches(propsEntries: IPropsFile[], compressedFileNode?: Node): Node[] {
  const root = new Node();
  for (const pe of propsEntries) {
    const { path } = pe;
    const segments = splitPathIntoSegments(path);
    let node = root;
    let currentPath = '';
    for (let i = 0; i < segments.length; i++) {
      const segment = segments[i];
      if (currentPath) {
        currentPath += BusinessConstants.pathSeparator;
      }
      currentPath += segment;
      let child = node.children.find((a) => a.label == segment);
      if (!child) {
        child = new Node();
        const key = buildNodeKey(currentPath, compressedFileNode?.key);
        child.label = segment;
        child.key = key;
        if (i == segments.length - 1) {
          child.type = 'unknown';
          if (pe.startTime) {
            child.startSeconds = moment.duration(pe.startTime)
              .asSeconds();
          }
          if (pe.endTime) {
            child.endSeconds = moment.duration(pe.endTime)
              .asSeconds();
          }
        } else {
          child.type = 'directory';
        }
        child.isCompressedFileContent = !!compressedFileNode;
        child.parent = node;
        node.children.push(child);
      }
      node = child;
    }
  }

  for (const node of root.children) {
    compressTree(node);
  }

  return root.children;
}

function expandTreeToLeaves(node: Node): Node[] {
  if (node.children.length == 0) {
    return [node];
  } else {
    const result: Node[] = [];
    for (const child of node.children) {
      result.push(...expandTreeToLeaves(child));
    }
    return result;
  }
}

const MediaPlayer = (props: IProps) => {
  const {
    defaultActiveIndex = 0,
    files: propFiles = [],
    interval = 1000,
    afterClose = () => {
    },
    renderOperations = (): any => {
    },
    autoPlay = false,
    ...otherProps
  } = props;
  useTraceUpdate(props, '[MediaPlayer]');
  const { t } = useTranslation();
  const forceUpdate = useUpdate();
  const log = buildLogger('MediaPlayer');

  const [visible, setVisible] = useState(true);

  const imageRef = useRef<HTMLImageElement | null>(null);


  const [fileTree, setFileTree] = useState<Node>();
  const fileTreeRef = useRef<Node | undefined>(fileTree);

  const nodeMapRef = useRef<Record<string, Node>>({});
  const videoSizeRef = useRef<{ width: number; height: number }>();
  const mediaContainerRef = useRef<HTMLDivElement | null>(null);
  const [extensionsMediaTypes, setExtensionsMediaTypes] = useState<Map<string | undefined, number>>(new Map<string, number>());
  const [playing, setPlaying] = useState(autoPlay);
  const playerRef = useRef<ReactPlayer | null>(null);

  const [progress, setProgress] = useState(0);
  const progressRef = useRef(0);
  const progressIntervalRef = useRef<any>();
  const activeDtRef = useRef<Date | undefined>();
  const autoGoToNextHandleRef = useRef<any>();

  const [fileListVisible, setFileListVisible] = useState(false);

  const [treeLeaves, setTreeLeaves] = useState<Node[]>([]);
  const [activeIndex, setActiveIndex] = useState(defaultActiveIndex);
  const activeNode = treeLeaves[activeIndex];

  const compressedFileEntriesRequestQueueRef = useRef(new Queue({
    concurrency: 5,
    autostart: true,
  }));

  useUpdateEffect(() => {
    fileTreeRef.current = fileTree;
    setTreeLeaves(fileTree ? expandTreeToLeaves(fileTree) : []);
  }, [fileTree]);

  useUpdateEffect(() => {
    log('treeLeaves changed', treeLeaves);
  }, [treeLeaves]);

  const [currentInitialized, setCurrentInitialized] = useState(false);

  useUpdateEffect(() => {
    progressRef.current = progress;
  }, [progress]);

  useEffect(() => {

  }, [fileListVisible]);

  useEffect(() => {
    const branches = buildBranches(propFiles);
    const tree = new Node();
    tree.children = branches;
    setFileTree(tree);
    nodeMapRef.current = buildNodeMap(branches);

    BApi.api.getAllExtensionMediaTypes()
      .then((t) => {
        const map = Object.keys(t.data)
          .reduce((s, ext) => {
            s[ext] = t.data[ext];
            return s;
          }, new Map<string, MediaType>());
        setExtensionsMediaTypes(map);
        console.log('[MediaPlayer]Extension-Media-Type initialized', map);
      });

    return () => {
      console.log('unmounting');
      clearProgressHandler();
    };
  }, []);

  const getMediaType = useCallback((file: string): MediaType => {
    const segments = file.split('.');
    // console.log(segments);
    if (segments.length == 1) {
      return MediaType.Unknown;
    } else {
      return extensionsMediaTypes[`.${segments[segments.length - 1]}`] ?? MediaType.Unknown;
    }
  }, [extensionsMediaTypes]);

  const gotoPrevNode = useCallback(() => {
    if (activeIndex > 0) {
      setActiveIndex(activeIndex - 1);
    }
  }, [activeIndex, treeLeaves]);

  const gotoNextNode = useCallback(() => {
    if (activeIndex < treeLeaves.length - 1) {
      setActiveIndex(activeIndex + 1);
    }
  }, [activeIndex, treeLeaves]);

  const clearProgressHandler = () => {
    clearInterval(progressIntervalRef.current);
    clearTimeout(autoGoToNextHandleRef.current);
  };

  useUpdateEffect(() => {
    console.log('current initialized', currentInitialized);
  }, [currentInitialized]);

  useEffect(() => {
    setPlaying(autoPlay);
    setProgress(0);
    setCurrentInitialized(false);
    clearProgressHandler();
  }, [activeNode]);

  const close = () => {
    setVisible(false);
    afterClose();
  };

  const tryAutoGotoNextNode = () => {
    if (autoPlay) {
      clearProgressHandler();
      activeDtRef.current = new Date();
      progressIntervalRef.current = setInterval(() => {
        const raw = ((new Date().getTime() - activeDtRef.current!.getTime()) / interval * 100);
        const percentage = Math.floor(raw);
        if (percentage != progressRef.current) {
          setProgress(percentage);
        }
      }, 100);
      autoGoToNextHandleRef.current = setTimeout(() => {
        gotoNextNode();
      }, Math.max(interval, 1000));
    }
  };

  const renderMedia = () => {
    // console.log(activeNode);
    if (!activeNode) {
      return;
    }
    const type = getMediaType(activeNode.key);
    switch (type) {
      case MediaType.Audio:
      case MediaType.Video:
        return (
          <ReactPlayer
            className={'media'}
            ref={(player) => {
              playerRef.current = player;
            }}
            onDuration={d => {
              // alert(d);
            }}
            width={videoSizeRef.current?.width}
            height={videoSizeRef.current?.height}
            onReady={(reactPlayer) => {
              const internalPlayer = reactPlayer.getInternalPlayer();

              const width = internalPlayer.videoWidth;
              const height = internalPlayer.videoHeight;
              if (width > 0 && height > 0) {
                videoSizeRef.current = {
                  width: Math.min(width, mediaContainerRef.current!.clientWidth),
                  height: Math.min(height, mediaContainerRef.current!.clientHeight),
                };
              }
              log('Video ready', reactPlayer, `${width}x${height}`);
              setCurrentInitialized(true);
            }}
            onPlay={() => {
              log('Video play');
              setPlaying(true);
            }}
            onPause={() => {
              log('Video pause');
              setPlaying(false);
            }}
            onSeek={() => {
              log('Video seek');
            }}
            onStart={() => {
              log('Video start');
              if (activeNode.startSeconds) {
                playerRef.current!.seekTo(activeNode.startSeconds);
              }
            }}
            onProgress={({
                           played,
                           playedSeconds,
                           loaded,
                           loadedSeconds,
                         }) => {
              // console.log(startSeconds, endSeconds, playedSeconds, activeIndex);
              if (activeNode.endSeconds && playedSeconds > activeNode.endSeconds) {
                tryAutoGotoNextNode();
              }
            }}
            onEnded={() => {
              tryAutoGotoNextNode();
            }}
            playing={playing}
            controls
            url={`${serverConfig.apiEndpoint}/file/play?fullname=${encodeURIComponent(activeNode.key)}`}
            config={{
              file: {
                attributes: {
                  crossOrigin: 'anonymous',
                },
              },
            }}
          />
        );
      case MediaType.Image:
        return (
          <img
            crossOrigin={'anonymous'}
            ref={imageRef}
            className={'media'}
            src={`${serverConfig.apiEndpoint}/file/play?fullname=${encodeURIComponent(activeNode.key)}`}
            onLoad={() => {
              setCurrentInitialized(true);
              tryAutoGotoNextNode();
            }}
          />
        );
      case MediaType.Text:
        return (
          <TextReader
            className={'media'}
            style={{ padding: '20px' }}
            file={activeNode.key}
            onLoad={() => {
              setCurrentInitialized(true);
              tryAutoGotoNextNode();
            }}
          />
        );
      default:
        return (
          <div
            className={'unsupported media'}
            onLoad={() => {
              setCurrentInitialized(true);
              tryAutoGotoNextNode();
            }}
          >{t('Unsupported')}
          </div>
        );
    }
  };

  const renderFile = (path: string) => {
    const compressedFileInnerFileSeparator = '!';
    const tmpSegments = path.split(compressedFileInnerFileSeparator);
    const segments: string[] = [];
    for (let i = 0; i < tmpSegments.length; i++) {
      const s = tmpSegments[i];
      if (!compressedFileExtensions.some(e => s.endsWith(e))) {
        const inner = tmpSegments[i + 1];
        segments.push(inner ? `${s}${compressedFileInnerFileSeparator}${inner}` : s);
        i++;
      } else {
        segments.push(s);
      }
    }

    let mainPath;
    let subPath;
    if (segments.length > 0) {
      mainPath = segments[0];
    }
    if (segments.length > 1) {
      subPath = segments[1];
    }
    if (segments.length > 2) {
      return `Unsupported path: ${path}`;
    }
    return (
      <div className={'file'}>
        <div className="icon">
          <FileSystemEntryIcon path={path} type={IconType.Dynamic} />
        </div>
        <div className="path">
          {mainPath}
          {subPath && (
            <>
              <CustomIcon type={'package'} size={'small'} />
              {subPath}
            </>
          )}
        </div>
      </div>
    );
  };

  /**
   * Fusion Design Tree will dirty data source with following properties, the behavior won't be stable unless we clear them manually.
   */
  const clearIceTreeMark = useCallback((node: Node) => {
    delete node['pos'];
    delete node['isLeaf'];
    delete node['isLastChild'];
    delete node['level'];
    if (node.children.length > 0) {
      node.children.forEach(clearIceTreeMark);
    }
  }, []);

  const onNodeLoaded = useCallback(async (node: Node) => {
    const skipCheckingContents = node.loadingInfoAsync || node.isCompressedFileContent || node.children.length > 0;
    if (node.type != 'unknown' && skipCheckingContents) {
      return;
    } else {
      compressedFileEntriesRequestQueueRef.current.push(async () => {
        const cts = new AbortController();
        node.loadingInfoAsync = cts;

        if (node.type == 'unknown') {
          log('Loading info for', node.key);
          const infoRsp = await BApi.file.getIwFsEntry({ path: node.key });
          const info = infoRsp.data;
          if (info) {
            // @ts-ignore
            if (info.type == IwFsType.Directory) {
              node.type = 'directory';
            } else {
              const ext = node.key.split('.').pop();
              if (ext && compressedFileExtensions.includes(ext)) {
                node.type = 'compressed-file';
              } else {
                node.type = 'file';
              }
            }
          }
        }

        if (!skipCheckingContents && node.type == 'compressed-file') {
          log('Loading compressed file entries for', node.key);
          const rsp = await BApi.file.getCompressedFileEntries({ compressedFilePath: node.key }, { signal: cts.signal });
          const files = (rsp.data || []);
          log('Compressed file entries loaded', node.key, files.length);
          if (files.length > 0) {
            // console.log(files, key, data.key);
            const subTrees = buildBranches(files.map(f => ({ path: f.path! })), node);
            const newNodeMap = buildNodeMap(subTrees);
            Object.assign(nodeMapRef.current, newNodeMap);

            node.children = subTrees;
            // log(subTrees);
            clearIceTreeMark(fileTreeRef.current!);
          }
        }

        forceUpdate();
      });
    }
  }, []);

  log('Rendering', treeLeaves, activeIndex, activeNode);

  if (visible) {
    return (
      <div
        onDoubleClick={(e) => {
          console.log('[MediaPlayer]Double clicked');
          e.stopPropagation();
          e.preventDefault();
        }}
        onKeyDown={(e) => {
          console.log(e, e.key);
          switch (e.key) {
            case 'ArrowLeft':
              gotoPrevNode();
              break;
            case 'ArrowRight':
              gotoNextNode();
              break;
            case 'F5':
            case 'R': {
              if (e.ctrlKey) {
                return;
              }
              break;
            }
            case 'Escape': {
              close();
              return;
            }
          }
          e.stopPropagation();
          e.preventDefault();
        }}
        className={'media-player'}
        onClick={(e) => {
        }}
        {...otherProps}
        ref={r => forceFocus(r)}
      >
        {(
          <>
            {(fileListVisible) ? (
              <Drawer
                visible
                placement={'left'}
                width={400}
                style={{ zIndex: 1005 }}
                onClose={() => setFileListVisible(false)}
              >
                {fileTree && (
                  <Tree
                    isLabelBlock
                    dataSource={fileTree.children}
                    expandedKeys={Object.keys(nodeMapRef.current)}
                    useVirtual
                    showLine
                    labelRender={(iceNode: any) => {
                      const {
                        label,
                        key,
                      } = iceNode;
                      const data = nodeMapRef.current[key];
                      // if (!data) {
                      //   console.error(key);
                      // }
                      // log(iceNode);
                      // log('Rendering tree node', iceNode, 'node data', data);
                      return (
                        <div
                          ref={r => {
                            if (r) {
                              onNodeLoaded(data);
                            } else {
                              log('Unloading', data.key);
                              // stopLoadingCompressedFileEntries(data);
                            }
                          }}
                          key={data.key}
                          title={label}
                          style={{
                            width: 200,
                            whiteSpace: 'nowrap',
                            overflow: 'hidden',
                            textOverflow: 'ellipsis',
                            display: 'flex',
                            alignItems: 'center',
                            gap: 5,
                          }}
                        >
                          <FileSystemEntryIcon
                            type={data.type == 'directory' ? IconType.Directory : IconType.Dynamic}
                            size={20}
                            path={data.key}
                          />
                          <span
                            style={{
                              whiteSpace: 'nowrap',
                              overflow: 'hidden',
                              textOverflow: 'ellipsis',
                            }}
                          >{label}</span>
                        </div>
                      );
                    }}
                    style={{
                      maxHeight: '90vh',
                      overflow: 'auto',
                    }}
                  />
                )}
              </Drawer>
            ) : (
              <div
                className={'file-list-portal'}
                onClick={() => {
                  setFileListVisible(true);
                }}
              >
                <Icon type={'list'} size={'xl'} />
              </div>
            )}
          </>
        )}

        <div
          className="media-container"
          onClick={() => {
            console.log('container clicked');
          }}
          ref={r => mediaContainerRef.current = r}
          tabIndex={0}
        >
          <div
            className="mask"
            onClick={() => {
              console.log('mask clicked');
              if (fileListVisible) {
                setFileListVisible(false);
              } else {
                close();
              }
            }}
          />
          {/* <div */}
          {/*   className={'media'} */}
          {/*   // ref={mediaRef} */}
          {/*   onClick={() => { */}
          {/*     console.log('media clicked'); */}
          {/*   }} */}
          {/* > */}
          {renderMedia()}
          {/* </div> */}
        </div>
        {activeNode && (
          <div
            className={'label'}
            onClick={() => {
              console.log('label clicked');
            }}
          >
            {renderFile(activeNode.key)}
            <span>({(treeLeaves.indexOf(activeNode)) + 1} / {treeLeaves.length})</span>
            {renderOperations && currentInitialized && renderOperations(activeNode.key, getMediaType(activeNode.key), playing, playerRef.current, imageRef.current)}
          </div>
        )}
        {autoPlay && progress && (
          <Progress percent={progress} size="small" textRender={() => ''} />
        )}
        {activeIndex > 0 && (
          <div
            className="left"
            onClick={gotoPrevNode}
          >
            <Icon type="arrow-left" size={'xl'} />
          </div>
        )}
        {activeIndex < treeLeaves.length - 1 && (
          <div
            className="right"
            onClick={gotoNextNode}
          >
            <Icon type="arrow-right" size={'xl'} />
          </div>
        )}
      </div>
    );
  }
  return null;
};

// MediaPlayer.show = createMediaPlayer;
MediaPlayer.show = (props: IProps) => createPortalOfComponent(MediaPlayer, props);
export default MediaPlayer;
