export async function readServerEvents(
  response: Response,
  onEvent: (name: string, data: string) => void,
): Promise<void> {
  const reader = response.body?.getReader();
  if (!reader) {
    return;
  }

  const decoder = new TextDecoder();
  let buffer = "";
  while (true) {
    const chunk = await reader.read();
    if (chunk.done) {
      break;
    }

    buffer += decoder.decode(chunk.value, { stream: true });
    buffer = buffer.replace(/\r\n/g, "\n");
    while (true) {
      const split = buffer.indexOf("\n\n");
      if (split < 0) {
        break;
      }

      const block = buffer.slice(0, split);
      buffer = buffer.slice(split + 2);
      let name = "message";
      const data: string[] = [];
      for (const line of block.split("\n")) {
        if (line.startsWith("event:")) {
          name = line.slice(6).trim();
        } else if (line.startsWith("data:")) {
          data.push(line.slice(5).trim());
        }
      }

      if (data.length > 0) {
        onEvent(name, data.join("\n"));
      }
    }
  }
}
