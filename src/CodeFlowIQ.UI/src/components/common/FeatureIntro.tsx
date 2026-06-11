import type { ReactNode } from "react";

type FeatureIntroProps = {
  title: string;
  description: string;
  helper?: string;
  actions?: ReactNode;
};

export function FeatureIntro({ title, description, helper, actions }: FeatureIntroProps) {
  return (
    <section className="feature-intro">
      <div>
        <h2>{title}</h2>
        <p>{description}</p>
        {helper && <small>{helper}</small>}
      </div>
      {actions && <div className="feature-intro-actions">{actions}</div>}
    </section>
  );
}
