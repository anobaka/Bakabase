import type { IconType } from "react-icons";

import { AiOutlineCloud, AiOutlineLink, AiOutlineThunderbolt } from "react-icons/ai";
import { FaMagnet } from "react-icons/fa";
import { SiBaidu, SiCloudflare, SiGoogledrive, SiMega, SiMicrosoftonedrive } from "react-icons/si";

import { AcquisitionDriveKind } from "@/sdk/constants";

export const driveOptions: {
  value: AcquisitionDriveKind;
  labelKey: string;
  icon: IconType;
}[] = [
  { value: AcquisitionDriveKind.Baidu, labelKey: "acquisition.drive.baidu", icon: SiBaidu },
  {
    value: AcquisitionDriveKind.Xunlei,
    labelKey: "acquisition.drive.xunlei",
    icon: AiOutlineThunderbolt,
  },
  {
    value: AcquisitionDriveKind.Feimao,
    labelKey: "acquisition.drive.feimao",
    icon: AiOutlineCloud,
  },
  {
    value: AcquisitionDriveKind.PikPak,
    labelKey: "acquisition.drive.pikpak",
    icon: AiOutlineCloud,
  },
  {
    value: AcquisitionDriveKind.OneOneFive,
    labelKey: "acquisition.drive.oneOneFive",
    icon: AiOutlineCloud,
  },
  { value: AcquisitionDriveKind.Mega, labelKey: "acquisition.drive.mega", icon: SiMega },
  {
    value: AcquisitionDriveKind.GoogleDrive,
    labelKey: "acquisition.drive.googleDrive",
    icon: SiGoogledrive,
  },
  {
    value: AcquisitionDriveKind.OneDrive,
    labelKey: "acquisition.drive.oneDrive",
    icon: SiMicrosoftonedrive,
  },
  {
    value: AcquisitionDriveKind.Cloudflare,
    labelKey: "acquisition.drive.cloudflare",
    icon: SiCloudflare,
  },
  {
    value: AcquisitionDriveKind.DirectUrl,
    labelKey: "acquisition.drive.directUrl",
    icon: AiOutlineLink,
  },
  { value: AcquisitionDriveKind.Magnet, labelKey: "acquisition.drive.magnet", icon: FaMagnet },
];

/** Preserve the preference order while discarding unknown and repeated entries. */
export const normalizePreferredDrives = (values: AcquisitionDriveKind[]) =>
  Array.from(new Set(values)).filter((value) =>
    driveOptions.some((option) => option.value === value),
  );
