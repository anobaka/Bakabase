import { Button, Collapse, Tag } from '@alifd/next';
import SimpleLabel from '@/components/SimpleLabel';
import './index.scss';
import CustomIcon from '@/components/CustomIcon';
import { useTranslation } from 'react-i18next';
import ClickableIcon from '@/components/ClickableIcon';

const { Panel } = Collapse;

export default () => {
  const { t } = useTranslation();

  return (
    <div className={'bulk-modification-page'}>
      <div className="header">
        <div className="title">Bulk Modification</div>
        <Button type={'primary'} size={'small'}>Create a bulk modification</Button>
      </div>
      <Collapse className={'bulk-modifications'} expandedKeys={['1']}>
        <Panel
          key={'1'}
          className={'bulk-modification'}
          title={(
            <div className={'title-bar'}>
              <div className="left">
                <div className="name">批量修改作者</div>
                <div className="resource-count">
                  涉及<span>38275</span>个资源
                </div>
                <div className="status">
                  <SimpleLabel status={'info'}>
                    待应用
                  </SimpleLabel>
                </div>
              </div>
              <div className="right">
                <div className="dt" title={t('Last modified at')}>
                  <CustomIcon type={'time'} size={'small'} />
                  2023-12-12 10:21:33
                </div>
              </div>
            </div>
          )}
        >
          <div className="filters-panel">
            <div className="title">
              Filters
            </div>
            <div className="content">
              <div className="filters">
                <div className="group">
                  <Tag.Closeable size={'small'}>作者包含abc</Tag.Closeable>
                  <ClickableIcon colorType={'normal'} type={'plus-circle'} size={'small'} />
                  <ClickableIcon colorType={'danger'} type={'delete'} size={'small'} />
                </div>
                并且
                <div className="group">
                  <div className="group">
                    <Tag.Closeable size={'small'}>作者包含xxx</Tag.Closeable>
                    <Tag.Closeable size={'small'}>标题包含yyy</Tag.Closeable>
                    <ClickableIcon colorType={'normal'} type={'plus-circle'} size={'small'} />
                    <ClickableIcon colorType={'danger'} type={'delete'} size={'small'} />
                  </div>
                  或者
                  <div className={'group'}>
                    <Tag.Closeable size={'small'}>语言是中文或日文</Tag.Closeable>
                    <Tag.Closeable size={'small'}>标题满足正则表达式.*mignon.*</Tag.Closeable>
                    <ClickableIcon colorType={'normal'} type={'plus-circle'} size={'small'} />
                    <ClickableIcon colorType={'danger'} type={'delete'} size={'small'} />
                  </div>
                  <ClickableIcon colorType={'normal'} type={'plus-circle'} size={'small'} />
                  <ClickableIcon colorType={'danger'} type={'delete'} size={'small'} />
                </div>
              </div>
              <div className="opts">
                <Button type={'normal'} size={'small'}>添加筛选条件</Button>
                <Button type={'primary'} size={'small'}>筛选</Button>
                <div className={'resource-count'}>
                  总计筛选出<span>38275</span>个资源
                </div>
                <Button type={'primary'} text size={'small'}>查看完整筛选结果</Button>
              </div>
            </div>
          </div>
          <div className="processes-panel">
            <div className="title">
              Processes
            </div>
            <div className="content">
              <div className="processes">
                <div className="process">
                  <div className="no">
                    {/* 1 */}
                    <SimpleLabel status={'default'}>1</SimpleLabel>
                  </div>
                  <div className="property">
                    <CustomIcon type={'segment'} size={'small'} />
                    名称
                  </div>
                  <div className="operation replace">
                    <CustomIcon type={'edit-square'} size={'small'} />
                    修改
                  </div>
                  <div className="value">
                    xxxxxxxxx
                  </div>
                </div>
                <div className="process">
                  <div className="no">
                    {/* 2 */}
                    <SimpleLabel status={'default'}>2</SimpleLabel>
                  </div>
                  <div className="property">
                    {/* <SimpleLabel status={'default'}>名称</SimpleLabel> */}
                    <CustomIcon type={'segment'} size={'small'} />
                    名称
                  </div>
                  <div className="operation merge">
                    <CustomIcon type={'git-merge-line'} size={'small'} />
                    合并
                  </div>
                  <div className="value">
                    xxxxxxxxx
                  </div>
                </div>
              </div>
              <div className="opts">
                <Button type={'normal'} size={'small'}>添加修改步骤</Button>
              </div>
            </div>
          </div>
          <div className="result-panel">
            <div className="title">
              Result
            </div>
            <div className="content">
              <div className="opts">
                <Button type={'normal'} size={'small'}>预览修改结果</Button>
                <Button type={'primary'} size={'small'}>执行</Button>
              </div>
            </div>
          </div>
        </Panel>
      </Collapse>
    </div>
  );
};
