import { describe, expect, it } from "vitest";
import { slugify } from "../../producer/text";
import { ADMIN_FIELDS, PRODUCER_FIELDS, formatPhone, validateFields, type SignupValues } from "./signup";

const valid: SignupValues = {
  producerType: "COMPANY",
  name: "Maré Alta",
  email: "contato@exemplo.test",
  phone: "(11) 98765-4321",
  adminName: "Pessoa Administradora",
  adminEmail: "admin@exemplo.test",
  adminPassword: "senha-de-teste",
};

const validate = (values: SignupValues) => validateFields([...PRODUCER_FIELDS, ...ADMIN_FIELDS], values);

describe("validação do cadastro", () => {
  it("aceita um cadastro completo", () => {
    expect(validate(valid)).toEqual({});
  });

  it("aponta cada campo inválido da produtora", () => {
    const errors = validateFields(PRODUCER_FIELDS, { ...valid, name: " ", email: "invalido", phone: "1199" });
    expect(Object.keys(errors)).toEqual(["name", "email", "phone"]);
  });

  it("aponta cada campo inválido do administrador", () => {
    const errors = validateFields(ADMIN_FIELDS, { ...valid, adminName: "", adminEmail: "x", adminPassword: "curta" });
    expect(Object.keys(errors)).toEqual(["adminName", "adminEmail", "adminPassword"]);
  });

  it("exige senha com o tamanho mínimo", () => {
    expect(validate({ ...valid, adminPassword: "1234567" }).adminPassword).toBeDefined();
    expect(validate({ ...valid, adminPassword: "12345678" }).adminPassword).toBeUndefined();
  });

  it("adapta a mensagem do nome ao tipo de produtor", () => {
    expect(validate({ ...valid, name: "" }).name).toBe("Informe o nome da produtora.");
    expect(validate({ ...valid, producerType: "INDIVIDUAL", name: "" }).name).toBe("Informe o seu nome.");
  });

  it("aceita telefone fixo e celular com DDD", () => {
    expect(validate({ ...valid, phone: "(11) 3265-4321" }).phone).toBeUndefined();
    expect(validate({ ...valid, phone: "98765-4321" }).phone).toBeDefined();
  });
});

describe("formatPhone", () => {
  it.each([
    ["", ""],
    ["1", "(1"],
    ["1198", "(11) 98"],
    ["1132654321", "(11) 3265-4321"],
    ["11987654321", "(11) 98765-4321"],
    ["119876543219999", "(11) 98765-4321"],
    ["(11) abc 9876", "(11) 9876"],
  ])("%s vira %s", (input, expected) => {
    expect(formatPhone(input)).toBe(expected);
  });
});

describe("slugify", () => {
  it("remove acentos e símbolos", () => {
    expect(slugify("  Maré Alta & Produções! ")).toBe("mare-alta-producoes");
  });
});
