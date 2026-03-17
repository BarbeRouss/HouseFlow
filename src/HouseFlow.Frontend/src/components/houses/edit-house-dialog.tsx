"use client";

import { useState, useEffect } from "react";
import { useTranslations } from "next-intl";
import { useUpdateHouse } from "@/lib/api/hooks";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Button } from "@/components/ui/button";

interface EditHouseDialogProps {
  houseId: string;
  house: {
    name: string;
    address?: string | null;
    zipCode?: string | null;
    city?: string | null;
  };
  open: boolean;
  onClose: () => void;
}

export function EditHouseDialog({ houseId, house, open, onClose }: EditHouseDialogProps) {
  const t = useTranslations("houses");
  const tCommon = useTranslations("common");

  const [name, setName] = useState(house.name);
  const [address, setAddress] = useState(house.address || "");
  const [zipCode, setZipCode] = useState(house.zipCode || "");
  const [city, setCity] = useState(house.city || "");

  useEffect(() => {
    if (open) {
      setName(house.name);
      setAddress(house.address || "");
      setZipCode(house.zipCode || "");
      setCity(house.city || "");
    }
  }, [open, house]);

  const updateMutation = useUpdateHouse(houseId, {
    onSuccess: () => {
      onClose();
    },
  });

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    updateMutation.mutate({
      name,
      address: address || null,
      zipCode: zipCode || null,
      city: city || null,
    });
  };

  if (!open) return null;

  return (
    <div className="fixed inset-0 bg-black/50 z-50 flex items-center justify-center p-4">
      <Card className="w-full max-w-lg">
        <CardHeader>
          <CardTitle>{t("editHouse")}</CardTitle>
        </CardHeader>
        <CardContent>
          <form onSubmit={handleSubmit} className="space-y-4">
            {updateMutation.isError && (
              <div className="bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 text-red-600 dark:text-red-400 px-4 py-3 rounded text-sm">
                {tCommon("createError")}
              </div>
            )}

            <div>
              <label htmlFor="edit-name" className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">
                {t("houseName")}
              </label>
              <input
                id="edit-name"
                type="text"
                value={name}
                onChange={(e) => setName(e.target.value)}
                required
                className="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-md shadow-sm focus:ring-2 focus:ring-blue-500 focus:border-blue-500 dark:bg-gray-700 dark:text-white"
              />
            </div>

            <div>
              <label htmlFor="edit-address" className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">
                {t("address")}
              </label>
              <input
                id="edit-address"
                type="text"
                value={address}
                onChange={(e) => setAddress(e.target.value)}
                className="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-md shadow-sm focus:ring-2 focus:ring-blue-500 focus:border-blue-500 dark:bg-gray-700 dark:text-white"
              />
            </div>

            <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
              <div>
                <label htmlFor="edit-zipCode" className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">
                  {t("zipCode")}
                </label>
                <input
                  id="edit-zipCode"
                  type="text"
                  value={zipCode}
                  onChange={(e) => setZipCode(e.target.value)}
                  className="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-md shadow-sm focus:ring-2 focus:ring-blue-500 focus:border-blue-500 dark:bg-gray-700 dark:text-white"
                />
              </div>
              <div>
                <label htmlFor="edit-city" className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">
                  {t("city")}
                </label>
                <input
                  id="edit-city"
                  type="text"
                  value={city}
                  onChange={(e) => setCity(e.target.value)}
                  className="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-md shadow-sm focus:ring-2 focus:ring-blue-500 focus:border-blue-500 dark:bg-gray-700 dark:text-white"
                />
              </div>
            </div>

            <div className="flex gap-3 pt-2">
              <Button type="button" variant="outline" onClick={onClose} className="flex-1">
                {tCommon("cancel")}
              </Button>
              <Button type="submit" disabled={updateMutation.isPending} className="flex-1">
                {updateMutation.isPending ? tCommon("loading") : tCommon("save")}
              </Button>
            </div>
          </form>
        </CardContent>
      </Card>
    </div>
  );
}
