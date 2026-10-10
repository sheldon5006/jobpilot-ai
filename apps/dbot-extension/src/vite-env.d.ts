/// <reference types="vite/client" />

interface ImportMetaEnv {
  /** HTTPS address of the hosted JobPilot API. Defaults to the local API. */
  readonly VITE_API_BASE_URL?: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}
