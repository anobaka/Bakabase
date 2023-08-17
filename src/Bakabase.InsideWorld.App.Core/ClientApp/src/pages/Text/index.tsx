import React, { useEffect, useState } from 'react';
import { Balloon, Button, Dialog, Grid, Icon, Input, Message, NumberPicker, Select, Table, Tag } from '@alifd/next';
import './index.scss';
import IceLabel from '@icedesign/label';
import i18n from 'i18next';
import { useTranslation } from 'react-i18next';
import { AddSpecialTextPrefabs, CreateSpecialText, DeleteSpecialText, GetAllSpecialText, UpdateSpecialText } from '../../sdk/apis';
import { ResourceLanguage, resourceLanguages, SpecialTextType, specialTextTypes } from '@/sdk/constants';
import CustomIcon from '@/components/CustomIcon';

const { Row, Col } = Grid;

const tagRenders = {
  Single: (t) => t.value1,
  Wrapper: (t) => `${t.value1}...${t.value2}`,
  Language: (t) => (
    <span className={'language'}>
      {t.value1}
      <IceLabel status="info" inverse={false}>{i18n.t(ResourceLanguage[t.value2])}</IceLabel>
    </span>
  ),
  TransformOrVolume: (t) => (
    <span className={'transformer'}>
      {t.value1}
      <Icon type="arrow-double-right" />
      {t.value2}
    </span>
  ),
};

const detailInputStyles = {
  Full: { width: '100%' },
  Half: { width: 350, margin: '0 10px' },
};

const renderDetail = (text, cb) => {
  console.log(text);
  const { value1, value2, type } = text;
  const values = [value1, value2];
  const elements = [];
  switch (type) {
    case SpecialTextType.Volume:
      elements.push(<Input
        key="0"
        placeholder="Text"
        required
        style={detailInputStyles.Half}
        onChange={(v) => values[0] = v}
        defaultValue={value1}
      />);
      elements.push(<NumberPicker
        key="1"
        defaultValue={value2}
        placeholder={i18n.t('Episode(null for number part of regex)')}
        style={detailInputStyles.Half}
        onChange={(v) => values[1] = v}
      />);
      break;
    case SpecialTextType.Useless:
    case SpecialTextType.Trim:
      elements.push(<Input
        key="0"
        placeholder="Text"
        required
        style={detailInputStyles.Full}
        defaultValue={value1}
        onChange={(v) => values[0] = v}
      />);
      break;
    case SpecialTextType.Language:
      elements.push(<Input
        key="0"
        placeholder="Text"
        required
        style={detailInputStyles.Half}
        onChange={(v) => values[0] = v}
        defaultValue={value1}
      />);
      elements.push(<Select
        key="1"
        dataSource={resourceLanguages.map((l) => ({ ...l, label: i18n.t(l.label) }))}
        defaultValue={value2 || ResourceLanguage.NotSet}
        style={detailInputStyles.Half}
        onChange={(v) => values[1] = v}
      />);
      break;
    case SpecialTextType.Wrapper:
    case SpecialTextType.Standardization:
      elements.push(<Input
        key="0"
        placeholder="Text"
        required
        style={detailInputStyles.Half}
        defaultValue={value1}
        onChange={(v) => values[0] = v}
      />);
      elements.push(<Input
        key="1"
        placeholder="Text"
        required
        style={detailInputStyles.Half}
        defaultValue={value2}
        onChange={(v) => values[1] = v}
      />);
      break;
    default:
      break;
  }

  Dialog.show({
    title: i18n.t(SpecialTextType[text.type]),
    content: <div style={{ width: 800, display: 'flex', justifyContent: 'center' }}>{elements}</div>,
    onOk: () => new Promise((resolve, reject) => {
      const Invoker = text.id > 0 ? UpdateSpecialText : CreateSpecialText;
      const model = {
        model: {
          value1: values[0],
          value2: values[1],
          type: text.type,
        },
      };
      if (text.id > 0) {
        model.id = text.id;
      }
      Invoker(model).invoke((a) => {
        if (!a.code) {
          resolve();
          if (cb) {
            cb();
          }
        } else {
          reject();
        }
      });
    }),
    closeable: true,
  });
};

const typeTagRendersMapping = {
  [SpecialTextType.Useless]: tagRenders.Single,
  [SpecialTextType.Language]: tagRenders.Language,
  [SpecialTextType.Wrapper]: tagRenders.Wrapper,
  [SpecialTextType.Standardization]: tagRenders.TransformOrVolume,
  [SpecialTextType.Volume]: tagRenders.TransformOrVolume,
  [SpecialTextType.Trim]: tagRenders.Single,
};

const typeDescriptions = {
  [SpecialTextType.Useless]: 'Text will be ignored if it surrounded by [wrappers]',
  [SpecialTextType.Language]: 'Text will be parsed as [specific language] if it surrounded by [wrappers]',
  [SpecialTextType.Wrapper]: 'General text surrounders',
  [SpecialTextType.Standardization]: 'Treat [text1] as [text2] during analyzation',
  [SpecialTextType.Volume]: 'Extract volume information from this text group',
  [SpecialTextType.Trim]: 'TBD, do not set it for now',
};

export default () => {
  const [texts, setTexts] = useState<any[]>([]);
  const { t } = useTranslation();

  useEffect(() => {
    loadData();
  }, []);

  const loadData = () => {
    GetAllSpecialText().invoke((t) => {
      const { data } = t;
      const ts = specialTextTypes.map((t) => {
        const list = data[t.label];
        list.sort((a, b) => a.value1.localeCompare(b.value1));
        return {
          type: t.label,
          value: t.value,
          texts: list,
        };
      });
      console.log(ts);
      setTexts(ts);
    });
  };

  // console.log(texts);

  return (
    <div className="text-page" title="Text">
      <Table
        dataSource={texts}
      >
        <Table.Column
          dataIndex={'type'}
          title={t('Type')}
          className={'type'}
          width={'20%'}
          cell={(c) => (
            <div style={{ display: 'flex', alignItems: 'center' }}>
              <span style={{ marginRight: 3 }}>{t(c)}</span>
              <Balloon.Tooltip
                align={'r'}
                trigger={<CustomIcon type={'question-circle'} />}
              >{t(typeDescriptions[SpecialTextType[c]])}
              </Balloon.Tooltip>
            </div>
          )}
        />
        <Table.Column
          dataIndex={'texts'}
          title={t('Texts')}
          className={'texts'}
          cell={(texts) => {
            return texts.map((c) => {
              const renderer = typeTagRendersMapping[c.type] ?? tagRenders.Single;
              return (
                <Tag.Closeable
                  key={c.id}
                  size={'small'}
                  onClick={() => renderDetail(c, () => loadData())}
                  onClose={() => {
                    if (confirm(t('Sure to delete?'))) {
                      DeleteSpecialText({
                        id: c.id,
                      }).invoke((a) => {
                        loadData();
                      });
                      return true;
                    }
                    return false;
                  }}
                >{renderer(c)}
                </Tag.Closeable>
              );
            });
          }}
        />
        <Table.Column
          dataIndex={'value'}
          title={t('Opt')}
          width={'5%'}
          cell={(id) => (
            <>
              <Button type="primary" onClick={() => renderDetail({ type: id }, () => loadData())} text>
                {t('Add')}
              </Button>
            </>
          )}
        />
      </Table>
      <div className={'opt'}>
        <Button
          type={'normal'}
          onClick={() => {
            AddSpecialTextPrefabs().invoke((a) => {
              if (!a.code) {
                loadData();
                Message.success('Success');
              }
            });
          }}
        >{t('Add prefabs')}
        </Button>
      </div>
    </div>
  );
};
