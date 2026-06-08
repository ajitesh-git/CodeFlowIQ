type RuntimeInjection = {
  baseUrl?: string;
};

declare global {
  interface Window {
    codeFlowIQRuntime?: RuntimeInjection;
  }
}

export type RuntimeConnection = {
  baseUrl: string;
  source: "desktop-runtime" | "environment" | "saved" | "default";
};

export function getDefaultRuntimeConnection(): RuntimeConnection {
  const injectedBaseUrl = window.codeFlowIQRuntime?.baseUrl;
  if (injectedBaseUrl) {
    return { baseUrl: injectedBaseUrl, source: "desktop-runtime" };
  }

  const environmentBaseUrl = import.meta.env.VITE_CODEFLOWIQ_API_BASE_URL as string | undefined;
  if (environmentBaseUrl) {
    return { baseUrl: environmentBaseUrl, source: "environment" };
  }

  const savedBaseUrl = localStorage.getItem("codeflowiq.apiBaseUrl");
  if (savedBaseUrl) {
    return { baseUrl: savedBaseUrl, source: "saved" };
  }

  return { baseUrl: "http://127.0.0.1:5188", source: "default" };
}
