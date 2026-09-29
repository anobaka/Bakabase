import { ManagedServersPanel } from "../components/ManagedServers";
import { ManagementAccessPanel } from "../components/ManagementAccess";

import { useDevicesPage } from "./context";
import TabHeading from "./TabHeading";

/**
 * Management: full control, both ways — the devices this one manages (its window switches
 * to theirs), and who may manage this one. Library sharing is a different permission with
 * different codes, and lives in its own tab.
 */
export default function ManageTab() {
  const { data, anchor, revealed } = useDevicesPage();

  return (
    <>
      <TabHeading introKey="federation.devices.tabIntro.manage" />
      <ManagedServersPanel
        collapseAdd
        addHighlighted={revealed === "add-server"}
        addRequested={anchor === "add-server"}
        headingLevel={3}
        highlighted={revealed === "servers"}
        source={{
          view: data.servers,
          error: data.serversError,
          loading: data.serversLoading,
          load: data.loadServers,
        }}
      />
      <ManagementAccessPanel
        headingLevel={3}
        highlighted={revealed === "management"}
        source={{ settings: data.access, error: data.accessError, load: data.loadAccess }}
        // Turning remote access on shows in library sharing too, and in the device tab.
        onChanged={() => void data.reload(["sharing"])}
      />
    </>
  );
}
