import { useState, type FormEvent } from "react";
import { useMutation } from "@tanstack/react-query";
import { useNavigate } from "react-router-dom";
import { organizationsApi } from "../../api/organizations";
import { OrganizationType, OrganizationTypeLabels, enumOptions } from "../../api/types";
import { ErrorMessage } from "../../components/Feedback";
import { ui } from "../../components/ui";

export function OrganizationCreatePage() {
  const navigate = useNavigate();
  const [name, setName] = useState("");
  const [type, setType] = useState<OrganizationType>(OrganizationType.Customer);

  const mutation = useMutation({
    mutationFn: () => organizationsApi.create({ name, type }),
    onSuccess: (org) => navigate(`/organizations/${org.id}`),
  });

  function handleSubmit(e: FormEvent) {
    e.preventDefault();
    mutation.mutate();
  }

  return (
    <div className="max-w-xl">
      <h1 className={ui.pageTitle}>Ny organisation</h1>
      <form onSubmit={handleSubmit} className={`${ui.card} space-y-4 p-6`}>
        <div>
          <label className={ui.label}>Namn</label>
          <input className={`${ui.input} w-full`} value={name} onChange={(e) => setName(e.target.value)} required />
        </div>
        <div>
          <label className={ui.label}>Typ</label>
          <select className={ui.input} value={type} onChange={(e) => setType(Number(e.target.value) as OrganizationType)}>
            {enumOptions(OrganizationTypeLabels).map((opt) => (
              <option key={opt.value} value={opt.value}>{opt.label}</option>
            ))}
          </select>
        </div>
        {mutation.isError && <ErrorMessage error={mutation.error} />}
        <div className="flex gap-2">
          <button type="submit" className={ui.buttonPrimary} disabled={mutation.isPending}>
            {mutation.isPending ? "Skapar..." : "Skapa organisation"}
          </button>
          <button type="button" className={ui.buttonSecondary} onClick={() => navigate(-1)}>Avbryt</button>
        </div>
      </form>
    </div>
  );
}
