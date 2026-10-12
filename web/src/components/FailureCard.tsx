import type { FailureCard as FailureCardModel, FailureFix } from "../modules/types";
import { Button } from "./Button";

export function FailureCard({
  failure,
  onFix,
}: {
  failure: FailureCardModel;
  onFix: (fix: FailureFix) => void;
}) {
  return (
    <section className="failure" role="alert">
      <h2>{failure.summary}</h2>
      <p>{failure.detail}</p>
      {failure.fixes.length > 0 && (
        <div className="actions">
          {failure.fixes.map((fix) => (
            <Button key={fix.id} variant="primary" onClick={() => onFix(fix)}>
              {fix.label}
            </Button>
          ))}
        </div>
      )}
    </section>
  );
}
