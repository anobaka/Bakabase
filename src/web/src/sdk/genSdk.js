import * as path from "node:path";
import * as process from "node:process";
import * as fs from "node:fs";
import { generateApi } from "swagger-typescript-api";

const IMPORTS = `import { buildLogger, extractErrorMessage } from "@/components/utils.tsx";
import { toast } from "@/components/bakaui";
`;

const LOG = `
const log = buildLogger("BApi");
`;

const BASE_RESPONSE_TYPE = `type BaseResponse = {
  code: number;
  message: string;
};
`;

const SHOW_ERROR_TOAST = `  showErrorToast?: (response: BaseResponse) => boolean;`

const bt = String.fromCharCode(96);       // 等价于 `
const dollar = "$";
const curlyOpen = "{";
const curlyClose = "}";

async function generateSdk() {
  try {

    const outputPath = path.resolve(process.cwd(), "./src/sdk");
    const outputFile = path.resolve(outputPath, "Api.ts");

    console.log(`Generating SDK...${outputPath}`);

    await generateApi({
      url: "http://127.0.0.1:8080/internal-doc/swagger/v1/swagger.json",
      name: "Api.ts",
      output: outputPath,
      unwrapResponseData: true,
      generateUnionEnums: true,
      modular: false,
    });

    // 在生成的文件第一行插入导入语句
    const apiFilePath = outputFile;
    if (fs.existsSync(apiFilePath)) {
      let content = fs.readFileSync(apiFilePath, 'utf-8');

      // 在文件开头添加导入语句
      content = IMPORTS + LOG + content;

      content = content.replace(
        'export type ResponseFormat = keyof Omit<Body, "body" | "bodyUsed">;',
        'export type ResponseFormat = keyof Omit<Body, "body" | "bodyUsed">;\n\n' + BASE_RESPONSE_TYPE
      );

      content = content.replace(
        'cancelToken?: CancelToken;',
        'cancelToken?: CancelToken;\n\n' + SHOW_ERROR_TOAST
      );

      content = content.replace(
        'public request = async <T = any, E = any>({',
        'public request = async <T = any, E = any>(fullRequestParams: FullRequestParams): Promise<T> => {\n    const {'
      )

      content = content.replace(
        '}: FullRequestParams): Promise<T> => {',
        '  } = fullRequestParams;'
      )

      const errorTitleTpl = '`${params.method} ${params.path} failed`'
      const apiErrorTitleTpl = '`[${response.code}]${params.method} ${params.path}`'
      content = content.replace(
        'if (!response.ok) throw data;\n      return data.data;\n    });\n  };',
        `
      if (!response.ok) {
        this.processResponseError(data, fullRequestParams);
        throw data;
      }
      return this.processResponseData(data.data as BaseResponse, fullRequestParams);
    }).catch((error) => {
      this.processResponseError(error, fullRequestParams);
      throw error;
    });
  };

  protected processResponseError = (error: any, params: FullRequestParams) => {
    const title = ${errorTitleTpl};
    const description = extractErrorMessage(error);

    toast.danger({
      title,
      description,
    });
  };

  protected processResponseData = <T = any>(response: BaseResponse, params: FullRequestParams): T => {
    // Check if the response has a code property (API response structure)
    if (response && typeof response === "object" && "code" in response) {
      log('[response]', params.path, response)
      switch (response.code) {
        case 0:
          break;
        default:
          const showErrorToast = params.showErrorToast || ((error: BaseResponse) => error.code >= 400 || error.code < 200);
          if (showErrorToast(response)) {
            const title = ${apiErrorTitleTpl};

            toast.danger({
              title,
              description: response.message,
            });
          }
      }
    }

    return response as T;
  };

        `

      )

      fs.writeFileSync(apiFilePath, content, 'utf-8');
      console.log("SDK generated successfully!");
    }

  } catch (error) {
    console.error(error);
    process.exit(1);
  }
}

generateSdk();
