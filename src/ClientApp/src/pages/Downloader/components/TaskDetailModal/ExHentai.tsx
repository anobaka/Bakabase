import _ from 'lodash';
import { Input, Textarea } from '@/components/bakaui';
import { ExHentaiDownloadTaskType } from '@/sdk/constants';
import PageRange from '@/pages/Downloader/components/TaskDetail/PageRange';

type KeyNameMap = Record<string, string | undefined>;

type Props = {
  type: ExHentaiDownloadTaskType;
  keyNameMap?: KeyNameMap;
  onValueChange?: (keyNameMap: KeyNameMap) => void;
  isReadOnly?: boolean;
};

export default ({ type, keyNameMap = {}, onValueChange, isReadOnly }: Props) => {
  switch (type) {
    case ExHentaiDownloadTaskType.SingleWork:
    case ExHentaiDownloadTaskType.Torrent:
      return (
        <Textarea
          value={_.keys(keyNameMap).join('\n')}
          placeholder={`https://exhentai.org/g/xxxxx/xxxxx/
https://exhentai.org/g/xxxxx/xxxxx/
...`}
          onValueChange={v => {
            onValueChange?.(_.fromPairs(v.split('\n').map(line => [line, undefined])));
          }}
          isReadOnly={isReadOnly}
        />
      );
    case ExHentaiDownloadTaskType.Watched:
      return (
        <>
          <Input
            label={'Watched'}
            value={_.keys(keyNameMap)[0]}
            placeholder={'https://exhentai.org/watch'}
            isReadOnly={isReadOnly}
            onValueChange={v => {
              onValueChange?.(_.fromPairs([[v, undefined]]));
            }}
          />
        </>
      );
    case ExHentaiDownloadTaskType.List:
      return (
        <Input
          label={'Watched'}
          value={_.keys(keyNameMap)[0]}
          placeholder={'https://exhentai.org/xxxxxxx'}
          isReadOnly={isReadOnly}
          onValueChange={v => {
            onValueChange?.(_.fromPairs([[v, undefined]]));
          }}
        />
      );
    default:
      return null;
  }
};
