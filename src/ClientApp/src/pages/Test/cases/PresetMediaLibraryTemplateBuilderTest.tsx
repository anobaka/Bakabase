import { Button } from '@/components/bakaui';
import PresetTemplateBuilder from '@/pages/MediaLibraryTemplate/components/PresetTemplateBuilder';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';

export default () => {
  const { createPortal } = useBakabaseContext();
  return (
    <Button onPress={() => {
      createPortal(
        PresetTemplateBuilder, {},
      );
    }}
    >PresetMediaLibraryTemplateBuilder</Button>
  );
};
