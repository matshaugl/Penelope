import { Route, Routes } from "react-router-dom";
import { App } from "./App";
import { GenericModulePage } from "../pages/GenericModulePage";
import { ChatPage } from "../pages/ChatPage";
import { HomePage } from "../pages/HomePage";
import { PluginPage } from "../pages/PluginPage";
import { SystemPage } from "../pages/SystemPage";
import { ToolsPage } from "../pages/ToolsPage";

export function AppRoutes() {
  return (
    <Routes>
      <Route element={<App />}>
        <Route index element={<HomePage />} />
        <Route path="chat" element={<ChatPage />} />
        <Route path="system" element={<SystemPage />} />
        <Route path="tools" element={<ToolsPage />} />
        <Route path="tools/:id" element={<GenericModulePage />} />
        <Route path="*" element={<PluginPage />} />
      </Route>
    </Routes>
  );
}
