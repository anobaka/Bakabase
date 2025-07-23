interface EnvConfig {
  apiEndpoint: string;
}

export const envConfig: EnvConfig = {
  apiEndpoint: import.meta.env.VITE_API_ENDPOINT ?? '',
};

export default envConfig; 