import cnTemplateUrl from "./templates/resource-list.cn.csv?url";
import enTemplateUrl from "./templates/resource-list.en.csv?url";

export const getSharedListTemplate = (language: string) =>
  /^(cn|zh)(-|$)/i.test(language)
    ? { url: cnTemplateUrl, fileName: "获取任务清单模板.csv" }
    : { url: enTemplateUrl, fileName: "acquisition-task-template.csv" };
