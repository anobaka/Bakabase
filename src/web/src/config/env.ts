interface EnvConfig {
  apiEndpoint: string;
}

export const envConfig: EnvConfig = {
  apiEndpoint: import.meta.env.VITE_API_ENDPOINT || 'http://localhost:5000',
};

export default envConfig; 