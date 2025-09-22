import { Button } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import AfterFirstPlayOperationsModal from "@/pages/file-processor/components/AfterFirstPlayOperationsModal";

const AfterFirstPlayOperationsModalTest = () => {
  const { createPortal } = useBakabaseContext();
  return (
    <Button onPress={() => {
      createPortal(AfterFirstPlayOperationsModal, {
        entry: { path: '/path/to/file' },
      });
    }}>
      Open
    </Button >
  );
};

export default AfterFirstPlayOperationsModalTest;