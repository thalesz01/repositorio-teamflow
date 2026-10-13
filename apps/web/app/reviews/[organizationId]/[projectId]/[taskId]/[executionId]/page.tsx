import { DiffReview } from "../../../../../../features/review/DiffReview";

export default async function ReviewPage({ params }: { params: Promise<{ organizationId: string; projectId: string; taskId: string; executionId: string }> }) {
  return <DiffReview {...await params} />;
}
