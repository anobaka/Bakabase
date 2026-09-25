import HelpCenterButton from "@/components/HelpCenter/HelpCenterButton";

/**
 * The help for data sync — the help center's Multi-device topic, at its Data sync section. Every
 * data sync surface offers it next to its heading (spec §11.3).
 */
export default function DataSyncHelp() {
  return <HelpCenterButton section="dataSync" topic="multiDevice" />;
}
