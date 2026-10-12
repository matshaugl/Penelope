export type PluginUi = {
  mount: (element: HTMLElement, props: { path: string }) => () => void;
};

export async function loadModuleUi(entryUrl: string): Promise<PluginUi> {
  const loaded = (await import(/* @vite-ignore */ entryUrl)) as Partial<PluginUi>;
  if (typeof loaded.mount !== "function") {
    throw new Error("This tool's page could not be opened.");
  }

  return { mount: loaded.mount };
}
