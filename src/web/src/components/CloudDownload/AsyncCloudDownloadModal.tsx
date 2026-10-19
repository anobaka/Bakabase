import { lazy, Suspense, type ComponentProps } from "react";
import { Spinner } from "@/components/bakaui";

const CloudDownloadModal = lazy(() => import("./CloudDownloadModal"));

export default function AsyncCloudDownloadModal(props: ComponentProps<typeof CloudDownloadModal>) {
  return (
    <Suspense fallback={<Spinner />}>
      <CloudDownloadModal {...props} />
    </Suspense>
  );
}
