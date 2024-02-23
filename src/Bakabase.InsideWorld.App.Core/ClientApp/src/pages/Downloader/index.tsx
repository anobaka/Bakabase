import React, { useCallback, useEffect, useRef, useState } from 'react';

import './index.scss';
import { Balloon, Button, Dialog, Dropdown, Icon, Input, Menu, Progress, Tag } from '@alifd/next';
import moment from 'moment';
import { Axis, Chart, Interval, Legend, Tooltip } from 'bizcharts';
import { ControlledMenu, MenuItem, useMenuState } from '@szhsin/react-menu';
import { useUpdate, useUpdateEffect } from 'react-use';
import { useTranslation } from 'react-i18next';
import { AutoSizer, List } from 'react-virtualized';
import CustomIcon from '@/components/CustomIcon';
import '@szhsin/react-menu/dist/index.css';
import '@szhsin/react-menu/dist/transitions/slide.css';

import TaskCreation from '@/pages/Downloader/components/TaskDetail';
import NameIcon from '@/pages/Downloader/components/NameIcon';
import { GetAllThirdPartyRequestStatistics, OpenFileOrDirectory } from '@/sdk/apis';
import {
  bilibiliDownloadTaskTypes,
  DownloadTaskAction,
  DownloadTaskActionOnConflict,
  DownloadTaskDtoStatus,
  downloadTaskDtoStatuses,
  ExHentaiDownloadTaskType,
  exHentaiDownloadTaskTypes,
  pixivDownloadTaskTypes,
  ResponseCode,
  ThirdPartyId,
  thirdPartyIds,
  ThirdPartyRequestResultType,
} from '@/sdk/constants';
import Configurations from '@/pages/Downloader/components/Configurations';
import store from '@/store';
import BApi from '@/sdk/BApi';
import { buildLogger, equalsOrIsChildOf, useTraceUpdate } from '@/components/utils';
import SimpleLabel from '@/components/SimpleLabel';

const testTasks = [
  {
    key: '123121232312321321',
    lowerCasedThirdPartyName: 1,
    name: 'eeeeeeee',
    percentage: 80,
  },
  {
    key: 'cxzkocnmaqwkodn wkjodas1',
    lowerCasedThirdPartyName: 2,
    name: 'pppppppppppp',
    percentage: 30,
  },
];

const DownloadTaskDtoStatusIceLabelStatusMap = {
  [DownloadTaskDtoStatus.Idle]: 'default',
  [DownloadTaskDtoStatus.InQueue]: 'default',
  [DownloadTaskDtoStatus.Downloading]: 'primary',
  [DownloadTaskDtoStatus.Failed]: 'danger',
  [DownloadTaskDtoStatus.Complete]: 'success',
  [DownloadTaskDtoStatus.Starting]: 'info',
  [DownloadTaskDtoStatus.Stopping]: 'info',
  [DownloadTaskDtoStatus.Disabled]: 'warning',
};

const DownloadTaskDtoStatusProgressBarColorMap = {
  [DownloadTaskDtoStatus.Idle]: '#666',
  [DownloadTaskDtoStatus.InQueue]: '#666',
  [DownloadTaskDtoStatus.Downloading]: '#5584ff',
  [DownloadTaskDtoStatus.Failed]: '#ff3000',
  [DownloadTaskDtoStatus.Complete]: '#46bc15',
  [DownloadTaskDtoStatus.Starting]: '#4494f9',
  [DownloadTaskDtoStatus.Stopping]: '#4494f9',
  [DownloadTaskDtoStatus.Disabled]: '#ff9300',
};

const RequestResultTypeIntervalColorMap = {
  [ThirdPartyRequestResultType.Succeed]: '#46bc15',
  [ThirdPartyRequestResultType.Failed]: '#ff3000',
  [ThirdPartyRequestResultType.Banned]: '#993300',
  [ThirdPartyRequestResultType.Canceled]: '#666',
  [ThirdPartyRequestResultType.TimedOut]: '#ff9300',
};

enum SelectionMode {
  Default,
  Ctrl,
  Shift,
}

interface ISearchForm {
  statuses?: DownloadTaskDtoStatus[];
  keyword?: string;
  thirdPartyIds?: ThirdPartyId[];
}

const log = buildLogger('DownloadPage');

export default () => {
  const { t } = useTranslation();
  const [taskId, setTaskId] = useState<number | undefined>(undefined);
  const forceUpdate = useUpdate();
  const [form, setForm] = useState<ISearchForm>({});
  const [configurationsVisible, setConfigurationsVisible] = useState(false);

  const gettingRequestStatistics = useRef(false);
  const [requestStatistics, setRequestStatistics] = useState([]);
  const requestStatisticsRef = useRef(requestStatistics);
  const [requestStatisticsChartVisible, setRequestStatisticsChartVisible] = useState(false);

  const tasks = store.useModelState('downloadTasks');

  const [selectedTaskIds, setSelectedTaskIds] = useState<number[]>([]);
  const selectedTaskIdsRef = useRef(selectedTaskIds);
  const selectionModeRef = useRef(SelectionMode.Default);

  const tasksRef = useRef(tasks);

  const clearTaskSelectionTargetsRef = useRef([]);

  const [menuProps, toggleMenu] = useMenuState();

  const tasksDomRef = useRef<HTMLUListElement>(null);

  const tasksElementRef = useRef<any>();

  useTraceUpdate({
    taskId,
    form,
    configurationsVisible,
    requestStatistics,
    requestStatisticsChartVisible,
    tasks,
    selectedTaskIds,
    menuProps,
  }, 'DownloaderPage');

  log('Rendering');

  const startTasksManually = async (ids?: number[], actionOnConflict = DownloadTaskActionOnConflict.NotSet) => {
    const rsp = await BApi.downloadTask.startDownloadTasks({ ids, actionOnConflict }, {
      ignoreError: rsp => rsp.code == ResponseCode.Conflict,
    });
    if (rsp.code == ResponseCode.Conflict) {
      Dialog.show({
        title: t('Found some conflicted tasks'),
        content: rsp.message,
        v2: true,
        width: 'auto',
        closeMode: ['mask', 'esc', 'close'],
        okProps: {
          children: t('Download selected tasks firstly'),
        },
        cancelProps: {
          children: t('Add selected tasks to the queue'),
        },
        onOk: async () => {
          return await BApi.downloadTask.startDownloadTasks({ ids, actionOnConflict: DownloadTaskActionOnConflict.StopOthers });
        },
        onCancel: async () => await BApi.downloadTask.startDownloadTasks({ ids, actionOnConflict: DownloadTaskActionOnConflict.Ignore }),
      });
    }
  };

  useUpdateEffect(() => {
    requestStatisticsRef.current = requestStatistics;
  }, [requestStatistics]);

  useUpdateEffect(() => {
    selectedTaskIdsRef.current = selectedTaskIds;
  }, [selectedTaskIds]);
  const contextMenuAnchorPointRef = useRef({
    x: 0,
    y: 0,
  });

  const renderContextMenu = useCallback(() => {
    if (selectedTaskIdsRef.current.length == 0) {
      return;
    }

    const moreThanOne = selectedTaskIdsRef.current.length > 1;

    return (
      <ControlledMenu
        {...menuProps}
        anchorPoint={contextMenuAnchorPointRef.current}
        className={'downloader-page-context-menu'}
        onClose={() => {
          toggleMenu(false);
        }}
      >
        <MenuItem onClick={() => {
          startTasksManually(selectedTaskIdsRef.current);
        }}
        >
          <div>
            <CustomIcon
              type={'play-circle'}
            />
            {moreThanOne && (
              <>
                {t('Bulk')}
                &nbsp;
              </>
            )}
            {t('Start')}
          </div>
        </MenuItem>
        <MenuItem onClick={() => BApi.downloadTask.stopDownloadTasks(selectedTaskIdsRef.current)}>
          <div>
            <CustomIcon
              type={'timeout'}
            />
            {moreThanOne && (
              <>
                {t('Bulk')}
                &nbsp;
              </>
            )}
            {t('Stop')}
          </div>
        </MenuItem>
        <MenuItem onClick={() => {
          BApi.downloadTask.removeDownloadTasksByIds(selectedTaskIdsRef.current);
        }}
        >
          <div className={'danger'}>
            <CustomIcon
              type={'delete'}
            />
            {moreThanOne && (
              <>
                {t('Bulk')}
                &nbsp;
              </>
            )}
            {t('Delete')}
          </div>
        </MenuItem>
      </ControlledMenu>
    );
  }, [menuProps]);

  useEffect(() => {
    tasksRef.current = tasks;
  }, [tasks]);

  const addToClearTaskSelectionTargets = useCallback((dom) => {
    // console.log(dom);
    clearTaskSelectionTargetsRef.current.push(dom);
  }, []);

  useEffect(() => {
    const getRequestStatisticsInterval = setInterval(() => {
      if (!gettingRequestStatistics.current) {
        gettingRequestStatistics.current = true;
        GetAllThirdPartyRequestStatistics()
          .invoke((a) => {
            if (JSON.stringify(a.data) != JSON.stringify(requestStatisticsRef.current)) {
              setRequestStatistics(a.data);
            }
          })
          .finally(() => {
            gettingRequestStatistics.current = false;
          });
      }
    }, 1000);

    const onMouseDown = (e) => {
      // console.log(e.target, clearTaskSelectionTargetsRef.current);
      // if (clearTaskSelectionTargetsRef.current.includes(e.target)) {
      //   alert('should clear selected tasks');
      // } else {
      //   alert('should not clear selected tasks');
      // }
    };

    const onKeydown = (e: KeyboardEvent) => {
      const target = e.target as HTMLElement;
      console.log(e.target, target, tasksDomRef.current);
      if (equalsOrIsChildOf(e.target as HTMLElement, tasksDomRef.current)) {
        switch (e.key) {
          case 'Control':
            selectionModeRef.current = SelectionMode.Ctrl;
            break;
          case 'Shift':
            selectionModeRef.current = SelectionMode.Shift;
            break;
          case 'Escape':
            setSelectedTaskIds([]);
            break;
          case 'a':
            if (e.ctrlKey) {
              e.stopPropagation();
              e.preventDefault();
              setSelectedTaskIds(tasksRef.current?.map((t) => t.id) || []);
            }
            break;
        }
      }
    };

    const onKeyUp = (e) => {
      switch (e.key) {
        case 'Control':
          selectionModeRef.current = SelectionMode.Default;
          break;
        case 'Shift':
          selectionModeRef.current = SelectionMode.Default;
          break;
      }
    };

    window.addEventListener('keydown', onKeydown);
    window.addEventListener('keyup', onKeyUp);
    window.addEventListener('mousedown', onMouseDown);

    return () => {
      window.removeEventListener('keydown', onKeydown);
      window.removeEventListener('keyup', onKeyUp);
      window.removeEventListener('mousedown', onMouseDown);
      clearInterval(getRequestStatisticsInterval);
    };
  }, []);

  const onTaskClick = taskId => {
    switch (selectionModeRef.current) {
      case SelectionMode.Default:
        if (selectedTaskIds.includes(taskId) && selectedTaskIds.length == 1) {
          setSelectedTaskIds([]);
        } else {
          setSelectedTaskIds([taskId]);
        }
        break;
      case SelectionMode.Ctrl:
        if (selectedTaskIds.includes(taskId)) {
          setSelectedTaskIds(selectedTaskIds.filter((id) => id != taskId));
        } else {
          setSelectedTaskIds([...selectedTaskIds, taskId]);
        }
        break;
      case SelectionMode.Shift:
        if (selectedTaskIds.length == 0) {
          setSelectedTaskIds([taskId]);
        } else {
          const lastSelectedTaskId = selectedTaskIds[selectedTaskIds.length - 1];
          const lastSelectedTaskIndex = tasks.findIndex((t) => t.id == lastSelectedTaskId);
          const currentTaskIndex = tasks.findIndex((t) => t.id == taskId);
          const start = Math.min(lastSelectedTaskIndex, currentTaskIndex);
          const end = Math.max(lastSelectedTaskIndex, currentTaskIndex);
          setSelectedTaskIds(tasks.slice(start, end + 1)
            .map((t) => t.id));
        }
        break;
    }
  };

  const renderTaskName = (task) => {
    let types;
    let taskNameShouldBeTranslated = false;
    switch (task.thirdPartyId) {
      case ThirdPartyId.ExHentai:
        types = exHentaiDownloadTaskTypes;
        if (task.type == ExHentaiDownloadTaskType.Watched) {
          taskNameShouldBeTranslated = true;
        }
        break;
      case ThirdPartyId.Pixiv:
        types = pixivDownloadTaskTypes;
        break;
      case ThirdPartyId.Bilibili:
        types = bilibiliDownloadTaskTypes;
        break;
    }

    const type = types?.find((t) => t.value == task.type).label || 'Unknown';

    const displayTaskName = (taskNameShouldBeTranslated ? t(task.name) : task.name) ?? task.key;

    if (displayTaskName) {
      return `${t(type)}: ${displayTaskName ?? task.key}`;
    } else {
      return t(type);
    }
  };

  const taskFilters: ((task: any) => boolean)[] = [];
  if (form.thirdPartyIds && form.thirdPartyIds.length > 0) {
    taskFilters.push(t => form.thirdPartyIds!.includes(t.thirdPartyId));
  }
  if (form.statuses && form.statuses.length > 0) {
    taskFilters.push(t => form.statuses!.includes(t.status));
  }

  if (form.keyword != undefined && form.keyword.length > 0) {
    const lowerCaseKeyword = form.keyword.toLowerCase();
    taskFilters.push(t => t.name?.toLowerCase().includes(lowerCaseKeyword) || t.key.toLowerCase().includes(lowerCaseKeyword));
  }

  const filteredTasks = tasks.filter((a) => taskFilters.every(f => f(a)));

  const renderRequestStatisticsChart = () => {
    if (requestStatisticsChartVisible) {
      const thirdPartyRequestCounts = (requestStatistics || []).reduce<any[]>((s, t) => {
        Object.keys(t.counts || {})
          .forEach((r) => {
            s.push({
              id: t.id.toString(),
              name: ThirdPartyId[t.id],
              result: ThirdPartyRequestResultType[r],
              count: t.counts[r],
            });
          });
        return s;
      }, []);

      // const data = [
      //   {
      //     State: 'WY',
      //     小于5岁: 25635,
      //     '5至13岁': 1890,
      //     '14至17岁': 9314,
      //   },
      //   {
      //     State: 'DC',
      //     小于5岁: 30352,
      //     '5至13岁': 20439,
      //     '14至17岁': 10225,
      //   },
      //   // {
      //   //   State: 'VT',
      //   //   小于5岁: 38253,
      //   //   '5至13岁': 42538,
      //   //   '14至17岁': 15757,
      //   // },
      //   // {
      //   //   State: 'ND',
      //   //   小于5岁: 51896,
      //   //   '5至13岁': 67358,
      //   //   '14至17岁': 18794,
      //   // },
      //   // {
      //   //   State: 'AK',
      //   //   小于5岁: 72083,
      //   //   '5至13岁': 85640,
      //   //   '14至17岁': 22153,
      //   // },
      // ];
      //
      // const ds = new DataSet();
      // const dv = ds.createView()
      //   .source(data);
      // dv.transform({
      //   type: 'fold',
      //   fields: ['小于5岁', '5至13岁', '14至17岁'],
      //   // 展开字段集
      //   key: '年龄段',
      //   // key字段
      //   value: '人口数量',
      //   // value字段
      //   retains: ['State'], // 保留字段集，默认为除fields以外的所有字段
      // });

      // console.log(dv.rows, data, thirdPartyRequestCounts);

      return (
        <Dialog
          visible
          onClose={() => setRequestStatisticsChartVisible(false)}
          onCancel={() => setRequestStatisticsChartVisible(false)}
          onOk={() => setRequestStatisticsChartVisible(false)}
          closeable
          footerActions={['ok']}
          title={t('Requests overview')}
          style={{ minWidth: 800 }}
        >
          <Chart
            height={300}
            data={thirdPartyRequestCounts}
            autoFit
          >
            <Interval
              adjust={[
                {
                  type: 'stack',
                },
              ]}
              color={[
                'result',
                (result) => RequestResultTypeIntervalColorMap[ThirdPartyRequestResultType[result]],
              ]}
              position={'id*count'}
            />
            <Axis
              name={'id'}
              label={{
                formatter: (id, item, index) => {
                  return ThirdPartyId[id];
                },
              }}
            />
            <Tooltip
              shared
              title={'name'}
            />
            <Legend name={'result'} />
          </Chart>
        </Dialog>
      );
    } else {

    }
  };

  // console.log(selectedTaskIds);

  return (
    <div className={'downloader-page'} ref={r => tasksElementRef.current = r}>
      {renderRequestStatisticsChart()}
      {renderContextMenu()}
      {configurationsVisible && (
        <Configurations
          onSaved={() => {
            setConfigurationsVisible(false);
          }}
          onClose={() => {
            setConfigurationsVisible(false);
          }}
        />
      )}
      {(taskId !== undefined) && (
        <TaskCreation
          taskId={taskId}
          onCreatedOrUpdated={() => {
            setTaskId(undefined);
          }}
          onClose={() => {
            setTaskId(undefined);
          }}
        />
      )}
      <div
        className="panel-and-form"
        ref={addToClearTaskSelectionTargets}
      >
        {tasks.length > 0 && (
          <div className="form">
            <div className="label">
              {t('Source')}
            </div>
            <div className="sources value">
              {thirdPartyIds.map((s) => {
                const count = tasks.filter((t) => t.thirdPartyId == s.value && (!(form.statuses?.length > 0) || form.statuses.includes(t.status))).length;
                return (
                  <Tag.Selectable
                    disabled={count == 0}
                    key={s.value}
                    className={'source'}
                    onChange={(checked) => {
                      let thirdPartyIds = form.thirdPartyIds || [];
                      if (checked) {
                        if (thirdPartyIds.every((a) => a != s.value)) {
                          thirdPartyIds.push(s.value);
                        }
                      } else {
                        thirdPartyIds = thirdPartyIds.filter((a) => a != s.value);
                      }
                      setForm({
                        ...form,
                        thirdPartyIds,
                      });
                    }}
                    size={'small'}
                    checked={form.thirdPartyIds?.some((a) => a == s.value)}
                  >
                    <img src={NameIcon[s.value]} alt="" />
                    <span>{s.label}{count > 0 && <span className={'count'}>({count})</span>}</span>
                  </Tag.Selectable>
                );
              })}
            </div>
            <div className="label">
              {t('Status')}
            </div>
            <div className="value statuses">
              {downloadTaskDtoStatuses.map(((s) => {
                const count = tasks.filter((t) => t.status == s.value && (!(form.thirdPartyIds?.length > 0) || form.thirdPartyIds.includes(t.thirdPartyId))).length;
                return (
                  <Tag.Selectable
                    disabled={count == 0}
                    key={s.value}
                    className={'status'}
                    onChange={(checked) => {
                      let statuses = form.statuses || [];
                      if (checked) {
                        if (statuses.every((a) => a != s.value)) {
                          statuses.push(s.value);
                        }
                      } else {
                        statuses = statuses.filter((a) => a != s.value);
                      }
                      setForm({
                        ...form,
                        statuses,
                      });
                    }}
                    size={'small'}
                    checked={form.statuses?.some((a) => a == s.value)}
                  >
                    {t(s.label)}{count > 0 && <span className={'count'}>({count})</span>}
                  </Tag.Selectable>
                );
              }))}
            </div>
            <div className="label">
              {t('Keyword')}
            </div>
            <div className="value keyword">
              <Input
                innerAfter={
                  <Icon
                    type="search"
                    size="xs"
                    style={{ margin: 4 }}
                  />
                }
                size={'small'}
                onChange={keyword => setForm({
                  ...form,
                  keyword,
                })}
              />
            </div>
          </div>
        )}
        <div
          className="panel"
          ref={addToClearTaskSelectionTargets}
        >
          <div className="left">
            <Button
              type={'secondary'}
              size={'small'}
              onClick={() => {
                setTaskId(0);
              }}
            >
              <>
                <CustomIcon type={'plus-circle'} size={'small'} />
                {t('Create task')}
              </>
            </Button>
            <Button
              type={'normal'}
              size={'small'}
              onClick={() => {
                startTasksManually(undefined, DownloadTaskActionOnConflict.Ignore);
              }}
            >
              <CustomIcon type={'play-circle'} size={'small'} />
              {t('Start all')}
            </Button>
            <Button
              type={'normal'}
              size={'small'}
              onClick={() => {
                BApi.downloadTask.stopDownloadTasks([]);
              }}
            >
              <CustomIcon type={'timeout'} size={'small'} />
              {t('Stop all')}
            </Button>

            {tasks?.length > 0 && (
              <div
                className="request-overview"
                onClick={() => {
                  setRequestStatisticsChartVisible(true);
                }}
              >
                <div className={'title'}>{t('Requests overview')}</div>
                {requestStatistics?.map((rs) => {
                  let successCount = 0;
                  let failureCount = 0;
                  Object.keys(rs.counts || {})
                    .forEach((r) => {
                      switch (parseInt(r)) {
                        case ThirdPartyRequestResultType.Succeed:
                          successCount += rs.counts[r];
                          break;
                        default:
                          failureCount += rs.counts[r];
                          break;
                      }
                    });
                  return (
                    <div className={'third-party'}>
                      <SimpleLabel status={'info'}>
                        {ThirdPartyId[rs.id]}
                      </SimpleLabel>
                      <div className={'statistics'}>
                        <Balloon.Tooltip
                          trigger={(
                            <span className={'success-count'}>{successCount}</span>
                          )}
                          align={'t'}
                        >
                          {t('Success')}
                        </Balloon.Tooltip>
                        /
                        <Balloon.Tooltip
                          trigger={(
                            <span className={'failure-count'}>{failureCount}</span>
                          )}
                          align={'t'}
                        >
                          {t('failure')}
                        </Balloon.Tooltip>
                      </div>
                    </div>
                  );
                })}
              </div>
            )}
          </div>
          <div className="right">
            <Button
              type={'normal'}
              size={'small'}
              onClick={() => {
                setConfigurationsVisible(true);
              }}
            >
              {t('Configurations')}
            </Button>
          </div>
        </div>
      </div>
      {tasks?.length > 0 ? (
        <div
          className={'tasks'}
        >
          <AutoSizer>
            {({ width, height }) => (
              <List
                // onScroll={onChildScroll}
                // isScrolling={isScrolling}
                // scrollTop={scrollTop}
                overscanRowCount={2}
                // scrollToIndex={scrollToIndex}
                width={width}
                height={height}
                // autoHeight
                rowCount={filteredTasks.length}
                rowHeight={75}
                rowRenderer={({
                                index,
                                style,
                                isVisible,
                                isScrolling,
                              }) => {
                  const task = filteredTasks[index];
                  const hasErrorMessage = task.status == DownloadTaskDtoStatus.Failed && task.message;
                  const selected = selectedTaskIds.indexOf(task.id) > -1;
                  return (
                    <div
                      key={task.id}
                      onContextMenu={e => {
                        console.log(`Opening context menu from ${task.id}:${task.name}`);
                        e.preventDefault();
                        if (!selectedTaskIdsRef.current.includes(task.id)) {
                          setSelectedTaskIds([task.id]);
                        }
                        contextMenuAnchorPointRef.current = {
                          x: e.clientX,
                          y: e.clientY,
                        };
                        toggleMenu(true);
                        forceUpdate();
                      }}
                      className={`download-item ${selected ? 'selected' : ''}`}
                      style={style}
                      onClick={() => onTaskClick(task.id)}
                    >
                      <div className="icon">
                        <img src={NameIcon[task.thirdPartyId]} />
                      </div>
                      <div className="content">
                        <div className="name">
                          <Balloon.Tooltip
                            trigger={(
                              <span onClick={() => {
                                setTaskId(task.id);
                              }}
                              >
                                {renderTaskName(task)}
                              </span>
                            )}
                            triggerType={'hover'}
                            align={'t'}
                          >
                            {task.key}
                          </Balloon.Tooltip>
                        </div>
                        <div className="info">
                          <div className="left">
                            <SimpleLabel
                              status={DownloadTaskDtoStatusIceLabelStatusMap[task.status]}
                              className={hasErrorMessage ? 'has-error-message' : ''}
                            >
                              <span
                                onClick={() => {
                                  if (hasErrorMessage) {
                                    Dialog.error({
                                      v2: true,
                                      width: 1000,
                                      title: t('Error'),
                                      content: (
                                        <pre>{task.message}</pre>
                                      ),
                                    });
                                  }
                                }}
                              >
                                {t(DownloadTaskDtoStatus[task.status])}
                              </span>
                            </SimpleLabel>
                            {(task.status == DownloadTaskDtoStatus.Downloading || task.status == DownloadTaskDtoStatus.Starting || task.status == DownloadTaskDtoStatus.Stopping) && (
                              <Icon type={'loading'} size={'small'} />
                            )}
                            <span>{task.current}</span>
                          </div>
                          <div className="right">
                            {task.failureTimes > 0 && (
                              <SimpleLabel
                                status={'danger'}
                                className={'failure-times'}
                              >
                                {t('Failure times')}:
                                <span>{task.failureTimes}</span>
                              </SimpleLabel>
                            )}
                            {task.nextStartDt && (
                              <SimpleLabel
                                status={'info'}
                                className={'next-start-dt'}
                              >
                                {t('Next start time')}:
                                <span>
                                  {moment(task.nextStartDt)
                                    .format('YYYY-MM-DD HH:mm:ss')}
                                </span>
                              </SimpleLabel>
                            )}
                          </div>
                        </div>
                        <div className="progress">
                          <Progress
                            // state={t.status == DownloadTaskStatus.Failed ? 'error' : 'normal'}
                            className={'bar'}
                            percent={task.progress}
                            color={DownloadTaskDtoStatusProgressBarColorMap[task.status]}
                            size={'small'}
                            textRender={() => `${task.progress.toFixed(2)}%`}
                            // progressive={t.status != DownloadTaskStatus.Failed}
                          />
                        </div>
                      </div>
                      <div className="opt">
                        {task.availableActions?.map((a, i) => {
                          const action = parseInt(a);
                          switch (action) {
                            case DownloadTaskAction.StartManually:
                            case DownloadTaskAction.Restart:
                              return (
                                <CustomIcon
                                  key={i}
                                  type={a == DownloadTaskAction.Restart ? 'redo' : 'play_fill'}
                                  title={t('Start now')}
                                  onClick={() => {
                                    startTasksManually([task.id]);
                                  }}
                                />
                              );
                            case DownloadTaskAction.Disable:
                              return (
                                <CustomIcon
                                  key={i}
                                  type={'stop'}
                                  title={t('Disable')}
                                  onClick={() => {
                                    BApi.downloadTask.stopDownloadTasks([task.id]);
                                  }}
                                />
                              );
                          }
                          return;
                        })}
                        <CustomIcon
                          type={'folder-open'}
                          title={t('Open folder')}
                          onClick={() => {
                            OpenFileOrDirectory({
                              path: task.downloadPath,
                            })
                              .invoke();
                          }}
                        />
                        <Dropdown
                          className={'task-operations-dropdown'}
                          trigger={
                            <CustomIcon
                              type={'ellipsis'}
                            />
                          }
                          triggerType={['click']}
                        >
                          <Menu>
                            {/* <Menu.Item title={t(t.status == DownloadTaskStatus.Paused ? 'Click to enable' : 'Click to disable')}> */}
                            {/*   <div className={t.status == DownloadTaskStatus.Paused ? 'disabled' : 'enabled'}> */}
                            {/*     <CustomIcon */}
                            {/*       type={t.status == DownloadTaskStatus.Paused ? 'close-circle' : 'check-circle'} */}
                            {/*       onClick={() => { */}

                            {/*       }} */}
                            {/*     /> */}
                            {/*     {t(t.status == DownloadTaskStatus.Paused ? 'Disabled' : 'Enabled')} */}
                            {/*   </div> */}
                            {/* </Menu.Item> */}
                            <Menu.Item>
                              <div
                                className={'remove'}
                                onClick={() => {
                                  Dialog.confirm({
                                    title: t('Are you sure to delete it?'),
                                    onOk: () => BApi.downloadTask.removeDownloadTasksByIds([task.id]),
                                  });
                                }}
                              >
                                <CustomIcon type={'delete'} />
                                {t('Remove')}
                              </div>
                            </Menu.Item>
                          </Menu>
                        </Dropdown>
                      </div>
                    </div>
                  );
                }}
              />
            )}
          </AutoSizer>
          {/* )} */}
          {/* </WindowScroller> */}
        </div>

      ) : (
        <div className={'no-task-yet'}>
          <Button
            type={'primary'}
            size={'large'}
            onClick={() => {
              setTaskId(0);
            }}
          >
            {t('Create download task')}
          </Button>
        </div>
      )}
    </div>
  );
};
