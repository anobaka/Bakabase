const serverConfig = {
  apiEndpoint: import.meta.env.VITE_API_ENDPOINT || process.env.NEXT_PUBLIC_API_ENDPOINT,
};

export default serverConfig;
