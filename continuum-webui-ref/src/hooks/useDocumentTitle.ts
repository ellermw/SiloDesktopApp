import { useEffect } from "react";

import { APP_DOCUMENT_TITLE, formatDocumentTitle } from "@/lib/documentTitle";

export function useDocumentTitle(label?: string | null) {
  useEffect(() => {
    if (typeof document === "undefined") {
      return;
    }

    document.title = formatDocumentTitle(label);

    return () => {
      document.title = APP_DOCUMENT_TITLE;
    };
  }, [label]);
}
