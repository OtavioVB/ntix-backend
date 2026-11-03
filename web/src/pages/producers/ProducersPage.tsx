import "./producers.css";

import { useEffect, useState } from "react";
import { Acts } from "./sections/Acts";
import { BrandLab } from "./sections/BrandLab";
import { Finale, MobileCta, SiteFooter, SiteNav } from "./sections/Chrome";
import { Hero } from "./sections/Hero";
import { Journey } from "./sections/Journey";

/** Tela de conversão para novas produtoras de eventos. */
export function ProducersPage() {
  const [brandName, setBrandName] = useState("");
  const [signupComplete, setSignupComplete] = useState(false);

  // O conteúdo nasce depois do carregamento: o navegador não acha sozinho o destino de um link com #.
  useEffect(() => {
    if (window.location.hash) document.querySelector(window.location.hash)?.scrollIntoView();
  }, []);

  return (
    <>
      <title>Ntix para produtores: venda ingressos no hub e na sua ticketeria</title>
      <meta
        name="description"
        content="Venda ingressos no hub de eventos da Ntix, com rede de afiliados, carteira que explica cada movimento e ticketeria própria como serviço opcional."
      />
      <a className="n-skip-link" href="#cadastro">Ir para o cadastro</a>
      <SiteNav />
      <main>
        <Hero suggestedName={brandName} onSignupComplete={() => setSignupComplete(true)} />
        <BrandLab name={brandName} onNameChange={setBrandName} />
        <Acts />
        <Journey />
        <Finale />
      </main>
      <SiteFooter />
      <MobileCta signupComplete={signupComplete} />
    </>
  );
}
