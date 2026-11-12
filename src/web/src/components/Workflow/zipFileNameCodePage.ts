import { zipFileNameCodePages, type ZipFileNameCodePage } from "@/sdk/constants";

export function isZipFileNameCodePage(value: unknown): value is ZipFileNameCodePage {
  return zipFileNameCodePages.some((choice) => choice.value === value);
}
